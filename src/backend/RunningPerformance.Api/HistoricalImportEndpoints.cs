using System.Security.Claims;
using NpgsqlTypes;
using RunningPerformance.Api.Authentication;
using RunningPerformance.Api.Http;
using RunningPerformance.Application.FreeTier;
using RunningPerformance.Application.Ingestion;
using RunningPerformance.Infrastructure.Database;
using RunningPerformance.Infrastructure.Storage;

namespace RunningPerformance.Api.Features;

public static class HistoricalImportEndpoints
{
    public static IEndpointRouteBuilder MapHistoricalImportEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/ingestion-runs").WithTags("Ingestion");

        group.MapPost("/historical-csv", EnqueueHistoricalCsvAsync)
            .WithName("EnqueueHistoricalCsv")
            .Accepts<Stream>("text/csv")
            .Produces<CsvImportAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status507InsufficientStorage);
        group.MapGet("/{id:guid}", GetRunAsync)
            .WithName("GetIngestionRun")
            .Produces<IngestionRunResponse>()
            .Produces(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> EnqueueHistoricalCsvAsync(
        string? fileName,
        HttpRequest request,
        HttpContext httpContext,
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        SupabaseObjectStore objectStore,
        HistoricalImportOptions options,
        FreeTierQuotaGuard quotaGuard,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "csv_file_name_required",
                "A fileName query parameter is required.");
        }

        if (request.ContentType is null
            || !request.ContentType.StartsWith("text/csv", StringComparison.OrdinalIgnoreCase))
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "csv_content_type_required",
                "The request Content-Type must be text/csv.");
        }

        StagedRequestBody staged;
        try
        {
            staged = await RequestBodyStager.StageAsync(
                request.Body,
                request.ContentLength,
                options.MaxCsvBytes,
                "csv",
                cancellationToken);
        }
        catch (RequestBodyRejectedException exception)
        {
            var status = exception.Code == "csv_too_large"
                ? StatusCodes.Status413PayloadTooLarge
                : StatusCodes.Status400BadRequest;
            return Problem(status, exception.Code, exception.Message);
        }

        await using (staged)
        {
            var ownerId = principal.GetRequiredOwnerId();
            var sourceFileId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var safeName = SanitizeFileName(fileName);
            var newObjectPath = $"{ownerId:D}/csv/{sourceFileId:D}/{safeName}";
            var uploadedNewObject = false;
            try
            {
                await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
                Guid? storedObjectId = null;
                string? objectPath = null;
                long usedStorageBytes;
                long usedDatabaseBytes;
                await using (var command = session.Connection.CreateCommand())
                {
                    command.Transaction = session.Transaction;
                    command.CommandText = """
                        select id, object_path
                        from app.stored_objects
                        where sha256 = @sha256;

                        select coalesce(sum(size_bytes), 0)
                        from app.stored_objects;

                        select pg_database_size(current_database());
                        """;
                    command.Parameters.AddWithValue("sha256", staged.Sha256);
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    if (await reader.ReadAsync(cancellationToken))
                    {
                        storedObjectId = reader.GetGuid(0);
                        objectPath = reader.GetString(1);
                    }

                    await reader.NextResultAsync(cancellationToken);
                    await reader.ReadAsync(cancellationToken);
                    usedStorageBytes = reader.GetInt64(0);
                    await reader.NextResultAsync(cancellationToken);
                    await reader.ReadAsync(cancellationToken);
                    usedDatabaseBytes = reader.GetInt64(0);
                }

                var projectedDatabaseMb = checked((int)Math.Ceiling(
                    (usedDatabaseBytes + staged.SizeBytes * 4d) / (1024d * 1024d)));
                var databaseQuota = quotaGuard.EvaluateDatabase(projectedDatabaseMb);
                if (!databaseQuota.AllowsWrite)
                {
                    return Problem(
                        StatusCodes.Status507InsufficientStorage,
                        databaseQuota.Code,
                        "The preventive free-tier database limit blocks this import.");
                }

                if (!storedObjectId.HasValue)
                {
                    var projectedMb = checked((int)Math.Ceiling(
                        (usedStorageBytes + staged.SizeBytes) / (1024d * 1024d)));
                    var quota = quotaGuard.EvaluateStorage(projectedMb);
                    if (!quota.AllowsWrite)
                    {
                        return Problem(
                            StatusCodes.Status507InsufficientStorage,
                            quota.Code,
                            "The preventive free-tier storage limit blocks this import.");
                    }

                    await using var content = staged.OpenRead();
                    await objectStore.UploadAsync(
                        newObjectPath,
                        content,
                        staged.SizeBytes,
                        "text/csv",
                        cancellationToken);
                    uploadedNewObject = true;
                    storedObjectId = Guid.NewGuid();
                    objectPath = newObjectPath;
                    await using var insertObject = session.Connection.CreateCommand();
                    insertObject.Transaction = session.Transaction;
                    insertObject.CommandText = """
                        insert into app.stored_objects (
                          id, owner_id, bucket_id, object_path, sha256,
                          size_bytes, mime_type, retention_class)
                        values (
                          @id, @owner_id, @bucket_id, @object_path, @sha256,
                          @size_bytes, 'text/csv', 'source');
                        """;
                    insertObject.Parameters.AddWithValue("id", storedObjectId.Value);
                    insertObject.Parameters.AddWithValue("owner_id", ownerId);
                    insertObject.Parameters.AddWithValue("bucket_id", SupabaseObjectStore.Bucket);
                    insertObject.Parameters.AddWithValue("object_path", objectPath);
                    insertObject.Parameters.AddWithValue("sha256", staged.Sha256);
                    insertObject.Parameters.AddWithValue("size_bytes", staged.SizeBytes);
                    await insertObject.ExecuteNonQueryAsync(cancellationToken);
                }

                await using (var command = session.Connection.CreateCommand())
                {
                    command.Transaction = session.Transaction;
                    command.CommandText = """
                        insert into app.source_files (
                          id, owner_id, stored_object_id, source_kind,
                          original_name, receipt_method, status)
                        values (
                          @source_file_id, @owner_id, @stored_object_id, 'normalized_csv',
                          @original_name, 'historical_import', 'received');

                        insert into app.ingestion_runs (
                          id, owner_id, source_file_id, run_type, status,
                          tool_version, schema_version, correlation_id, item_count)
                        values (
                          @run_id, @owner_id, @source_file_id, 'csv_import', 'pending',
                          'running-performance-csv/1.0.0', @schema_version, @correlation_id, 0);

                        insert into app.ingestion_items (
                          owner_id, ingestion_run_id, ordinal, source_file_id,
                          status, action)
                        values (
                          @owner_id, @run_id, 1, @source_file_id,
                          'pending', 'awaiting_validation');
                        """;
                    command.Parameters.AddWithValue("source_file_id", sourceFileId);
                    command.Parameters.AddWithValue("owner_id", ownerId);
                    command.Parameters.AddWithValue("stored_object_id", storedObjectId.Value);
                    command.Parameters.AddWithValue("original_name", safeName);
                    command.Parameters.AddWithValue("run_id", runId);
                    command.Parameters.AddWithValue("schema_version", NormalizedActivityCsvContract.SchemaVersion);
                    command.Parameters.AddWithValue("correlation_id", httpContext.GetCorrelationId());
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await AuditWriter.WriteAsync(
                    session,
                    ownerId,
                    "historical_csv.enqueued",
                    "ingestion_run",
                    runId,
                    httpContext.GetCorrelationId(),
                    ["source_file_id", "status", "schema_version"],
                    cancellationToken);
                await session.CommitAsync(cancellationToken);

                return Results.Accepted(
                    $"/api/v1/ingestion-runs/{runId}",
                    new CsvImportAcceptedResponse(
                        runId,
                        sourceFileId,
                        staged.Sha256,
                        staged.SizeBytes,
                        "pending",
                        !uploadedNewObject));
            }
            catch
            {
                if (uploadedNewObject)
                {
                    try
                    {
                        await objectStore.RemoveAsync(newObjectPath, CancellationToken.None);
                    }
                    catch (ObjectStoreException)
                    {
                        // The unreferenced object is visible to operators through Storage inventory.
                    }
                }

                throw;
            }
        }
    }

    private static async Task<IResult> GetRunAsync(
        Guid id,
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        IngestionRunResponse? run = null;
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select
                  run.id, run.run_type, run.status, run.tool_version, run.schema_version,
                  run.correlation_id, run.started_at, run.finished_at,
                  run.item_count, run.success_count, run.failure_count,
                  run.attempt_count, run.heartbeat_at, run.created_at,
                  source.id, source.original_name, object.sha256, object.size_bytes
                from app.ingestion_runs as run
                left join app.source_files as source
                  on source.owner_id = run.owner_id and source.id = run.source_file_id
                left join app.stored_objects as object
                  on object.owner_id = source.owner_id and object.id = source.stored_object_id
                where run.id = @id;
                """;
            command.Parameters.AddWithValue("id", id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                run = new(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetGuid(5),
                    GetNullableDateTime(reader, 6),
                    GetNullableDateTime(reader, 7),
                    reader.GetInt32(8),
                    reader.GetInt32(9),
                    reader.GetInt32(10),
                    reader.GetInt32(11),
                    GetNullableDateTime(reader, 12),
                    reader.GetDateTime(13),
                    reader.IsDBNull(14) ? null : reader.GetGuid(14),
                    reader.IsDBNull(15) ? null : reader.GetString(15),
                    reader.IsDBNull(16) ? null : reader.GetString(16),
                    reader.IsDBNull(17) ? null : reader.GetInt64(17),
                    []);
            }
        }

        if (run is null)
        {
            return Results.NotFound();
        }

        var errors = new List<IngestionItemErrorResponse>();
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select ordinal, observed_key, status, error_code, error_message, retryable
                from app.ingestion_items
                where ingestion_run_id = @run_id
                  and error_code is not null
                order by ordinal
                limit 100;
                """;
            command.Parameters.AddWithValue("run_id", id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                errors.Add(new(
                    reader.GetInt32(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetBoolean(5)));
            }
        }

        await session.CommitAsync(cancellationToken);
        return Results.Ok(run with { Errors = errors });
    }

    private static string SanitizeFileName(string fileName)
    {
        var leaf = Path.GetFileName(fileName.Trim());
        var sanitized = new string(leaf
            .Select(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_'
                ? character
                : '_')
            .Take(100)
            .ToArray());
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "activities-normalized.csv";
        }

        return sanitized.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            ? sanitized
            : $"{sanitized}.csv";
    }

    private static DateTime? GetNullableDateTime(Npgsql.NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);

    private static IResult Problem(int status, string code, string detail) =>
        Results.Problem(
            statusCode: status,
            title: "Historical CSV import rejected",
            detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

public sealed record CsvImportAcceptedResponse(
    Guid RunId,
    Guid SourceFileId,
    string Sha256,
    long SizeBytes,
    string Status,
    bool ReusedStoredObject);

public sealed record IngestionItemErrorResponse(
    int Ordinal,
    string? ObservedKey,
    string Status,
    string Code,
    string Message,
    bool Retryable);

public sealed record IngestionRunResponse(
    Guid Id,
    string RunType,
    string Status,
    string ToolVersion,
    string SchemaVersion,
    Guid CorrelationId,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    int ItemCount,
    int SuccessCount,
    int FailureCount,
    int AttemptCount,
    DateTime? HeartbeatAt,
    DateTime CreatedAt,
    Guid? SourceFileId,
    string? OriginalName,
    string? Sha256,
    long? SizeBytes,
    IReadOnlyList<IngestionItemErrorResponse> Errors);
