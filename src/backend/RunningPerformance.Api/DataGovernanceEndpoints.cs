using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;
using RunningPerformance.Api.Authentication;
using RunningPerformance.Api.Http;
using RunningPerformance.Application.Dashboard;
using RunningPerformance.Application.FreeTier;
using RunningPerformance.Infrastructure.Database;
using RunningPerformance.Infrastructure.Storage;

namespace RunningPerformance.Api.Features;

public static class DataGovernanceEndpoints
{
    public static IEndpointRouteBuilder MapDataGovernanceEndpoints(this IEndpointRouteBuilder routes)
    {
        var exports = routes.MapGroup("/api/v1/exports").WithTags("Data governance");
        exports.MapGet("/", ListExportsAsync)
            .WithName("GetExports")
            .Produces<IReadOnlyList<ExportJobResponse>>();
        exports.MapPost("/", CreateExportAsync)
            .WithName("CreateExport")
            .Produces<ExportJobResponse>(StatusCodes.Status201Created)
            .Produces<ExportJobResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status413PayloadTooLarge);
        exports.MapGet("/{exportId:guid}/download", DownloadExportAsync)
            .WithName("DownloadExport")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status410Gone);

        var lifecycle = routes.MapGroup("/api/v1/lifecycle-requests").WithTags("Data governance");
        lifecycle.MapGet("/", ListLifecycleRequestsAsync)
            .WithName("GetLifecycleRequests")
            .Produces<IReadOnlyList<LifecycleRequestResponse>>();
        lifecycle.MapPost("/", CreateLifecycleRequestAsync)
            .WithName("CreateLifecycleRequest")
            .Produces<LifecycleRequestResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound);

        routes.MapPut("/api/v1/operations/quota-usage", RecordQuotaUsageAsync)
            .WithName("RecordFreeTierQuotaUsage")
            .WithTags("Operations")
            .Produces<FreeTierUsageReportResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        return routes;
    }

    private static async Task<IResult> ListExportsAsync(
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var exports = new List<ExportJobResponse>();
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select id, format, schema_version,
                  case when status = 'completed' and expires_at <= now()
                    then 'expired' else status end,
                  requested_at, completed_at, expires_at,
                  case when status = 'completed' and expires_at > now()
                    then '/api/v1/exports/' || id::text || '/download' end
                from app.export_jobs
                order by requested_at desc, id desc;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                exports.Add(ReadExport(reader));
            }
        }

        await session.CommitAsync(cancellationToken);
        return Results.Ok(exports);
    }

    private static async Task<IResult> CreateExportAsync(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        OwnerDataSource dataSource,
        SupabaseObjectStore objectStore,
        FreeTierQuotaGuard quotaGuard,
        CancellationToken cancellationToken)
    {
        if (!AthleteExportRules.IsValidIdempotencyKey(idempotencyKey))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Idempotency-Key"] = ["Idempotency-Key debe contener entre 8 y 200 caracteres."]
            });
        }

        var ownerId = principal.GetRequiredOwnerId();
        var normalizedKey = idempotencyKey!.Trim();
        var existing = await FindExportByKeyAsync(
            dataSource,
            ownerId,
            normalizedKey,
            cancellationToken);
        if (existing is not null)
        {
            return Results.Ok(existing);
        }

        string payload;
        long currentStorageBytes;
        int currentDatabaseMb;
        decimal? currentEgressGb;
        await using (var session = await dataSource.OpenAsync(ownerId, cancellationToken))
        {
            await using (var command = session.Connection.CreateCommand())
            {
                command.Transaction = session.Transaction;
                command.CommandText = "select app.build_athlete_export()::text;";
                payload = (string)(await command.ExecuteScalarAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Export payload was not generated."));
            }

            await using (var command = session.Connection.CreateCommand())
            {
                command.Transaction = session.Transaction;
                command.CommandText = "select database_bytes, storage_bytes from app.current_quota_usage();";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                await reader.ReadAsync(cancellationToken);
                currentDatabaseMb = (int)Math.Ceiling(reader.GetInt64(0) / 1024d / 1024d);
                currentStorageBytes = reader.GetInt64(1);
            }

            await using (var command = session.Connection.CreateCommand())
            {
                command.Transaction = session.Transaction;
                command.CommandText = """
                    select nullif(detail ->> 'egressGb', '')::numeric
                    from app.audit_events
                    where action = 'free_tier.usage_reported'
                    order by occurred_at desc, id desc
                    limit 1;
                    """;
                currentEgressGb = (decimal?)await command.ExecuteScalarAsync(cancellationToken);
            }

            await session.CommitAsync(cancellationToken);
        }

        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        if (payloadBytes.Length > AthleteExportRules.MaximumBytes)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "La exportación excede el límite seguro de 20 MB.",
                extensions: new Dictionary<string, object?> { ["code"] = "export_too_large" });
        }

        var databaseDecision = quotaGuard.EvaluateDatabase(currentDatabaseMb);
        var projectedStorageMb = (int)Math.Ceiling(
            (currentStorageBytes + payloadBytes.LongLength) / 1024d / 1024d);
        var storageDecision = quotaGuard.EvaluateStorage(projectedStorageMb);
        var projectedEgressGb = currentEgressGb is null
            ? null
            : currentEgressGb + payloadBytes.LongLength / 1024m / 1024m / 1024m;
        var egressDecision = quotaGuard.EvaluateEgress(projectedEgressGb);
        var blocked = new[] { databaseDecision, storageDecision, egressDecision }
            .FirstOrDefault(item => !item.AllowsWrite);
        if (blocked is not null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "La política de costo cero bloquea temporalmente la exportación.",
                detail: "Registra o libera cuota gratuita antes de generar un nuevo objeto.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = blocked.Code,
                    ["billingEnabled"] = blocked.BillingEnabled
                });
        }

        var exportId = Guid.NewGuid();
        var storedObjectId = Guid.NewGuid();
        var requestedAt = DateTimeOffset.UtcNow;
        var expiresAt = requestedAt.Add(AthleteExportRules.Retention);
        var objectPath = $"{ownerId:D}/export/{exportId:N}/running-performance-export-v1.json";
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(payloadBytes));
        await using (var stream = new MemoryStream(payloadBytes, writable: false))
        {
            await objectStore.UploadAsync(
                objectPath,
                stream,
                payloadBytes.LongLength,
                "application/json",
                cancellationToken);
        }

        try
        {
            await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
            await using (var command = session.Connection.CreateCommand())
            {
                command.Transaction = session.Transaction;
                command.CommandText = """
                    insert into app.stored_objects (
                      id, owner_id, bucket_id, object_path, sha256, size_bytes,
                      mime_type, retention_class, accepted_at)
                    values (
                      @stored_object_id, @owner_id, @bucket_id, @object_path,
                      @sha256, @size_bytes, 'application/json', 'temporary_export', @requested_at);

                    insert into app.export_jobs (
                      id, owner_id, format, schema_version, status, stored_object_id,
                      requested_at, completed_at, expires_at, idempotency_key)
                    values (
                      @export_id, @owner_id, @format, @schema_version, 'completed',
                      @stored_object_id, @requested_at, @requested_at, @expires_at,
                      @idempotency_key);

                    insert into app.audit_events (
                      owner_id, actor_id, actor_type, action, entity_type, entity_id,
                      correlation_id, changed_fields, detail)
                    values (
                      @owner_id, @owner_id, 'athlete', 'export.completed', 'export_job',
                      @export_id, @correlation_id,
                      array['format', 'schema_version', 'status', 'expires_at'],
                      jsonb_build_object(
                        'schemaVersion', @schema_version,
                        'sizeBytes', @size_bytes,
                        'expiresAt', @expires_at,
                        'publicUrlCreated', false,
                        'billingEnabled', false));
                    """;
                command.Parameters.AddWithValue("stored_object_id", storedObjectId);
                command.Parameters.AddWithValue("owner_id", ownerId);
                command.Parameters.AddWithValue("bucket_id", SupabaseObjectStore.Bucket);
                command.Parameters.AddWithValue("object_path", objectPath);
                command.Parameters.AddWithValue("sha256", sha256);
                command.Parameters.AddWithValue("size_bytes", payloadBytes.LongLength);
                command.Parameters.AddWithValue("requested_at", requestedAt);
                command.Parameters.AddWithValue("expires_at", expiresAt);
                command.Parameters.AddWithValue("export_id", exportId);
                command.Parameters.AddWithValue("format", AthleteExportRules.Format);
                command.Parameters.AddWithValue("schema_version", AthleteExportRules.SchemaVersion);
                command.Parameters.AddWithValue("idempotency_key", normalizedKey);
                command.Parameters.AddWithValue("correlation_id", httpContext.GetCorrelationId());
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await session.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await objectStore.RemoveAsync(objectPath, cancellationToken);
            existing = await FindExportByKeyAsync(
                dataSource,
                ownerId,
                normalizedKey,
                cancellationToken);
            if (existing is not null)
            {
                return Results.Ok(existing);
            }

            throw;
        }
        catch
        {
            await objectStore.RemoveAsync(objectPath, cancellationToken);
            throw;
        }

        var response = new ExportJobResponse(
            exportId,
            AthleteExportRules.Format,
            AthleteExportRules.SchemaVersion,
            "completed",
            requestedAt,
            requestedAt,
            expiresAt,
            $"/api/v1/exports/{exportId}/download");
        return Results.Created($"/api/v1/exports/{exportId}", response);
    }

    private static async Task<IResult> DownloadExportAsync(
        Guid exportId,
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        SupabaseObjectStore objectStore,
        CancellationToken cancellationToken)
    {
        var ownerId = principal.GetRequiredOwnerId();
        string? objectPath = null;
        long sizeBytes = 0;
        DateTimeOffset expiresAt = default;
        string? status = null;
        await using (var session = await dataSource.OpenAsync(ownerId, cancellationToken))
        {
            await using (var command = session.Connection.CreateCommand())
            {
                command.Transaction = session.Transaction;
                command.CommandText = """
                    select stored.object_path, stored.size_bytes, export.expires_at,
                      export.status
                    from app.export_jobs export
                    join app.stored_objects stored
                      on stored.owner_id = export.owner_id
                     and stored.id = export.stored_object_id
                    where export.id = @export_id;
                    """;
                command.Parameters.AddWithValue("export_id", exportId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    objectPath = reader.GetString(0);
                    sizeBytes = reader.GetInt64(1);
                    expiresAt = reader.GetFieldValue<DateTimeOffset>(2);
                    status = reader.GetString(3);
                }
            }

            await session.CommitAsync(cancellationToken);
        }

        if (objectPath is null)
        {
            return Results.NotFound();
        }

        if (status != "completed" || expiresAt <= DateTimeOffset.UtcNow)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status410Gone,
                title: "La exportación venció.",
                extensions: new Dictionary<string, object?> { ["code"] = "export_expired" });
        }

        if (sizeBytes > AthleteExportRules.MaximumBytes)
        {
            throw new InvalidOperationException("Stored export exceeds the configured safe limit.");
        }

        await using var destination = new MemoryStream((int)sizeBytes);
        await objectStore.DownloadToAsync(objectPath, destination, cancellationToken);
        return Results.File(
            destination.ToArray(),
            "application/json",
            $"running-performance-{exportId:N}.json",
            enableRangeProcessing: false);
    }

    private static async Task<ExportJobResponse?> FindExportByKeyAsync(
        OwnerDataSource dataSource,
        Guid ownerId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        ExportJobResponse? response = null;
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select id, format, schema_version,
                  case when status = 'completed' and expires_at <= now()
                    then 'expired' else status end,
                  requested_at, completed_at, expires_at,
                  case when status = 'completed' and expires_at > now()
                    then '/api/v1/exports/' || id::text || '/download' end
                from app.export_jobs
                where idempotency_key = @idempotency_key;
                """;
            command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                response = ReadExport(reader);
            }
        }

        await session.CommitAsync(cancellationToken);
        return response;
    }

    private static ExportJobResponse ReadExport(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetFieldValue<DateTimeOffset>(4),
        reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
        reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
        reader.IsDBNull(7) ? null : reader.GetString(7));

    private static async Task<IResult> ListLifecycleRequestsAsync(
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var requests = new List<LifecycleRequestResponse>();
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select id, request_type, scope::text, rationale, status,
                  approved_by, executed_at, evidence::text, created_at, updated_at
                from app.lifecycle_requests
                order by created_at desc, id desc;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                requests.Add(ReadLifecycleRequest(reader));
            }
        }

        await session.CommitAsync(cancellationToken);
        return Results.Ok(requests);
    }

    private static async Task<IResult> CreateLifecycleRequestAsync(
        CreateLifecycleRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.RequestType is not ("archive" or "delete"))
        {
            errors[nameof(request.RequestType)] = ["El tipo debe ser archive o delete."];
        }
        if (!AthleteExportRules.IsValidLifecycleScope(request.ScopeType, request.ScopeId))
        {
            errors[nameof(request.ScopeType)] = ["El alcance debe ser all sin ID o un recurso permitido con ID."];
        }
        if (!AthleteExportRules.IsValidLifecycleRationale(request.Rationale))
        {
            errors[nameof(request.Rationale)] = ["La justificación debe contener entre 12 y 2000 caracteres."];
        }
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        if (request.ScopeType != "all" && !await ScopeExistsAsync(session, request, cancellationToken))
        {
            return Results.NotFound();
        }

        var requestId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var scopeJson = JsonSerializer.Serialize(new
        {
            type = request.ScopeType,
            id = request.ScopeId
        });
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                insert into app.lifecycle_requests (
                  id, owner_id, request_type, scope, rationale, status,
                  created_at, updated_at)
                values (
                  @request_id, @owner_id, @request_type, @scope, @rationale,
                  'requested', @created_at, @created_at);

                insert into app.audit_events (
                  owner_id, actor_id, actor_type, action, entity_type, entity_id,
                  correlation_id, changed_fields, detail)
                values (
                  @owner_id, @owner_id, 'athlete', 'lifecycle.requested',
                  'lifecycle_request', @request_id, @correlation_id,
                  array['request_type', 'scope', 'rationale', 'status'],
                  jsonb_build_object(
                    'requestType', @request_type,
                    'scope', @scope,
                    'automaticExecution', false));
                """;
            command.Parameters.AddWithValue("request_id", requestId);
            command.Parameters.AddWithValue("owner_id", ownerId);
            command.Parameters.AddWithValue("request_type", request.RequestType);
            command.Parameters.Add(new NpgsqlParameter("scope", NpgsqlDbType.Jsonb) { Value = scopeJson });
            command.Parameters.AddWithValue("rationale", request.Rationale.Trim());
            command.Parameters.AddWithValue("created_at", createdAt);
            command.Parameters.AddWithValue("correlation_id", httpContext.GetCorrelationId());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await session.CommitAsync(cancellationToken);
        var response = new LifecycleRequestResponse(
            requestId,
            request.RequestType,
            JsonDocument.Parse(scopeJson).RootElement.Clone(),
            request.Rationale.Trim(),
            "requested",
            null,
            null,
            null,
            createdAt,
            createdAt);
        return Results.Created($"/api/v1/lifecycle-requests/{requestId}", response);
    }

    private static async Task<bool> ScopeExistsAsync(
        OwnerDbSession session,
        CreateLifecycleRequest request,
        CancellationToken cancellationToken)
    {
        var table = request.ScopeType switch
        {
            "activity" => "activities",
            "source_file" => "source_files",
            "training_plan" => "training_plans",
            "weekly_evaluation" => "weekly_evaluations",
            _ => throw new InvalidOperationException("Unsupported lifecycle scope.")
        };
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = $"select exists(select 1 from app.{table} where id = @scope_id);";
        command.Parameters.AddWithValue("scope_id", request.ScopeId!.Value);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static LifecycleRequestResponse ReadLifecycleRequest(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        JsonDocument.Parse(reader.GetString(2)).RootElement.Clone(),
        reader.GetString(3),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetGuid(5),
        reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
        reader.IsDBNull(7) ? null : JsonDocument.Parse(reader.GetString(7)).RootElement.Clone(),
        reader.GetFieldValue<DateTimeOffset>(8),
        reader.GetFieldValue<DateTimeOffset>(9));

    private static async Task<IResult> RecordQuotaUsageAsync(
        RecordFreeTierUsageRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        OwnerDataSource dataSource,
        FreeTierQuotaGuard guard,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.EgressGb is null && request.CiMinutes is null && request.BackendHours is null)
        {
            errors["usage"] = ["Debe reportarse al menos una cuota del proveedor."];
        }
        if (request.EgressGb < 0 || request.CiMinutes < 0 || request.BackendHours < 0)
        {
            errors["usage"] = ["Los consumos no pueden ser negativos."];
        }
        if (request.MeasuredAt > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            errors[nameof(request.MeasuredAt)] = ["La medición no puede estar en el futuro."];
        }
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var ownerId = principal.GetRequiredOwnerId();
        var reportId = Guid.NewGuid();
        var detail = JsonSerializer.Serialize(new
        {
            egressGb = request.EgressGb,
            ciMinutes = request.CiMinutes,
            backendHours = request.BackendHours,
            measuredAt = request.MeasuredAt,
            billingEnabled = false
        });
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                insert into app.audit_events (
                  id, owner_id, actor_id, actor_type, action, entity_type,
                  correlation_id, changed_fields, detail, occurred_at)
                values (
                  @id, @owner_id, @owner_id, 'athlete', 'free_tier.usage_reported',
                  'free_tier', @correlation_id,
                  array['egressGb', 'ciMinutes', 'backendHours', 'measuredAt'],
                  @detail, now());
                """;
            command.Parameters.AddWithValue("id", reportId);
            command.Parameters.AddWithValue("owner_id", ownerId);
            command.Parameters.AddWithValue("correlation_id", httpContext.GetCorrelationId());
            command.Parameters.Add(new NpgsqlParameter("detail", NpgsqlDbType.Jsonb) { Value = detail });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await session.CommitAsync(cancellationToken);
        return Results.Created(
            "/api/v1/operations/quota-usage",
            new FreeTierUsageReportResponse(
                reportId,
                request.EgressGb,
                request.CiMinutes,
                request.BackendHours,
                request.MeasuredAt,
                guard.EvaluateEgress(request.EgressGb).State.ToString().ToLowerInvariant(),
                guard.EvaluateCiMinutes(request.CiMinutes).State.ToString().ToLowerInvariant(),
                guard.EvaluateBackendHours(request.BackendHours).State.ToString().ToLowerInvariant(),
                false));
    }
}

public sealed record ExportJobResponse(
    Guid Id,
    string Format,
    string SchemaVersion,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt,
    string? DownloadHref);

public sealed record CreateLifecycleRequest(
    string RequestType,
    string ScopeType,
    Guid? ScopeId,
    string Rationale);

public sealed record LifecycleRequestResponse(
    Guid Id,
    string RequestType,
    JsonElement Scope,
    string Rationale,
    string Status,
    Guid? ApprovedBy,
    DateTimeOffset? ExecutedAt,
    JsonElement? Evidence,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record RecordFreeTierUsageRequest(
    decimal? EgressGb,
    decimal? CiMinutes,
    decimal? BackendHours,
    DateTimeOffset MeasuredAt);

public sealed record FreeTierUsageReportResponse(
    Guid Id,
    decimal? EgressGb,
    decimal? CiMinutes,
    decimal? BackendHours,
    DateTimeOffset MeasuredAt,
    string EgressState,
    string CiState,
    string BackendState,
    bool BillingEnabled);
