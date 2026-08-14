using System.Security.Claims;
using Npgsql;
using NpgsqlTypes;
using RunningPerformance.Api.Authentication;
using RunningPerformance.Api.Http;
using RunningPerformance.Application.FreeTier;
using RunningPerformance.Application.Ingestion;
using RunningPerformance.Fit;
using RunningPerformance.Infrastructure.Database;
using RunningPerformance.Infrastructure.Storage;
using RunningPerformance.Infrastructure.Sync;

namespace RunningPerformance.Api.Features;

public static class FitIngestionEndpoints
{
    public static IEndpointRouteBuilder MapFitIngestionEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/ingestion-runs/fit", EnqueueManualAsync)
            .WithName("EnqueueFit")
            .WithTags("Ingestion")
            .Accepts<Stream>("application/vnd.ant.fit")
            .Produces<FitImportAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status507InsufficientStorage);
        routes.MapPost("/api/v1/ingestion-runs/{id:guid}/reprocess", ReprocessAsync)
            .WithName("ReprocessFit")
            .WithTags("Ingestion")
            .Produces<FitImportAcceptedResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound);

        routes.MapPost("/api/v1/sync-clients/pairing-tokens", CreatePairingTokenAsync)
            .WithName("CreateSyncPairingToken")
            .WithTags("Sync clients")
            .Produces<PairingTokenResponse>();
        routes.MapGet("/api/v1/sync-clients", ListSyncClientsAsync)
            .WithName("ListSyncClients")
            .WithTags("Sync clients")
            .Produces<IReadOnlyList<SyncClientResponse>>();
        routes.MapDelete("/api/v1/sync-clients/{id:guid}", RevokeSyncClientAsync)
            .WithName("RevokeSyncClient")
            .WithTags("Sync clients")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        routes.MapPost("/api/v1/sync/pair", ExchangePairingTokenAsync)
            .WithName("ExchangeSyncPairingToken")
            .WithTags("FIT sync")
            .AllowAnonymous()
            .Produces<DeviceCredentialResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
        routes.MapPost("/api/v1/sync/fit", EnqueueSyncAsync)
            .WithName("EnqueueSynchronizedFit")
            .WithTags("FIT sync")
            .AllowAnonymous()
            .Accepts<Stream>("application/vnd.ant.fit")
            .Produces<FitImportAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        return routes;
    }

    private static Task<IResult> EnqueueManualAsync(
        string? fileName,
        long? garminActivityId,
        HttpRequest request,
        HttpContext httpContext,
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        SupabaseObjectStore objectStore,
        FitIngestionOptions options,
        FreeTierQuotaGuard quotaGuard,
        CancellationToken cancellationToken) =>
        EnqueueAsync(
            principal.GetRequiredOwnerId(),
            null,
            "manual",
            fileName,
            garminActivityId,
            request,
            httpContext,
            dataSource,
            objectStore,
            options,
            quotaGuard,
            cancellationToken);

    private static async Task<IResult> EnqueueSyncAsync(
        string? fileName,
        long? garminActivityId,
        HttpRequest request,
        HttpContext httpContext,
        SyncCredentialService credentialService,
        OwnerDataSource dataSource,
        SupabaseObjectStore objectStore,
        FitIngestionOptions options,
        FreeTierQuotaGuard quotaGuard,
        CancellationToken cancellationToken)
    {
        var client = await credentialService.AuthenticateAsync(
            request.Headers.Authorization,
            cancellationToken);
        if (client is null || !client.Scopes.Contains("fit.upload", StringComparer.Ordinal))
        {
            return Problem(
                StatusCodes.Status401Unauthorized,
                "fit_sync_credential_invalid",
                "A valid, unexpired fit.upload credential is required.");
        }
        if (string.IsNullOrWhiteSpace(request.Headers["Idempotency-Key"]))
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "idempotency_key_required",
                "Synchronized FIT uploads require an Idempotency-Key header.");
        }
        return await EnqueueAsync(
            client.OwnerId,
            client.ClientId,
            "incremental",
            fileName,
            garminActivityId,
            request,
            httpContext,
            dataSource,
            objectStore,
            options,
            quotaGuard,
            cancellationToken);
    }

    private static async Task<IResult> EnqueueAsync(
        Guid ownerId,
        Guid? syncClientId,
        string receiptMethod,
        string? fileName,
        long? garminActivityId,
        HttpRequest request,
        HttpContext httpContext,
        OwnerDataSource dataSource,
        SupabaseObjectStore objectStore,
        FitIngestionOptions options,
        FreeTierQuotaGuard quotaGuard,
        CancellationToken cancellationToken)
    {
        if (garminActivityId is null or <= 0)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "garmin_activity_id_required",
                "A positive Garmin activity ID from the download context is required.");
        }
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "fit_file_name_required",
                "A fileName query parameter is required.");
        }
        if (request.ContentType is null
            || !(request.ContentType.StartsWith("application/vnd.ant.fit", StringComparison.OrdinalIgnoreCase)
                 || request.ContentType.StartsWith("application/octet-stream", StringComparison.OrdinalIgnoreCase)))
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "fit_content_type_required",
                "The request Content-Type must be application/vnd.ant.fit or application/octet-stream.");
        }

        if (!TryNormalizeIdempotencyKey(
                request.Headers["Idempotency-Key"],
                out var normalizedIdempotencyKey))
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "idempotency_key_invalid",
                "Idempotency-Key must contain at most 128 safe ASCII characters.");
        }
        StagedRequestBody staged;
        try
        {
            staged = await RequestBodyStager.StageAsync(
                request.Body,
                request.ContentLength,
                options.MaxFitBytes,
                "fit",
                cancellationToken);
        }
        catch (RequestBodyRejectedException exception)
        {
            return Problem(
                exception.Code == "fit_too_large"
                    ? StatusCodes.Status413PayloadTooLarge
                    : StatusCodes.Status400BadRequest,
                exception.Code,
                exception.Message);
        }

        await using (staged)
        {
            if (!HasFitSignature(staged.Path))
            {
                return Problem(
                    StatusCodes.Status400BadRequest,
                    "fit_signature_invalid",
                    "The upload does not contain a FIT header signature.");
            }

            if (normalizedIdempotencyKey is not null)
            {
                var existing = await FindIdempotentRunAsync(
                    ownerId,
                    normalizedIdempotencyKey,
                    dataSource,
                    cancellationToken);
                if (existing is not null)
                {
                    if (existing.GarminActivityId != garminActivityId.Value
                        || !string.Equals(existing.Sha256, staged.Sha256, StringComparison.Ordinal)
                        || existing.SizeBytes != staged.SizeBytes)
                    {
                        return Problem(
                            StatusCodes.Status409Conflict,
                            "idempotency_key_payload_mismatch",
                            "Idempotency-Key was already used for different FIT content or Garmin activity ID.");
                    }
                    return Results.Accepted(
                        $"/api/v1/ingestion-runs/{existing.RunId}",
                        existing with { ReusedReceipt = true });
                }
            }

            var sourceFileId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var safeName = SanitizeFitFileName(fileName);
            var newObjectPath = $"{ownerId:D}/fit/{sourceFileId:D}/{safeName}";
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
                        select id, object_path from app.stored_objects where sha256 = @sha256;
                        select coalesce(sum(size_bytes), 0) from app.stored_objects;
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
                    (usedDatabaseBytes + staged.SizeBytes * 25d) / (1024d * 1024d)));
                var databaseQuota = quotaGuard.EvaluateDatabase(projectedDatabaseMb);
                if (!databaseQuota.AllowsWrite)
                {
                    return Problem(
                        StatusCodes.Status507InsufficientStorage,
                        databaseQuota.Code,
                        "The preventive free-tier database limit blocks detailed FIT ingestion.");
                }

                if (!storedObjectId.HasValue)
                {
                    var projectedStorageMb = checked((int)Math.Ceiling(
                        (usedStorageBytes + staged.SizeBytes) / (1024d * 1024d)));
                    var storageQuota = quotaGuard.EvaluateStorage(projectedStorageMb);
                    if (!storageQuota.AllowsWrite)
                    {
                        return Problem(
                            StatusCodes.Status507InsufficientStorage,
                            storageQuota.Code,
                            "The preventive free-tier Storage limit blocks this FIT upload.");
                    }
                    await using var content = staged.OpenRead();
                    await objectStore.UploadAsync(
                        newObjectPath,
                        content,
                        staged.SizeBytes,
                        "application/vnd.ant.fit",
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
                          @size_bytes, 'application/vnd.ant.fit', 'source');
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
                          id, owner_id, stored_object_id, source_kind, original_name,
                          receipt_method, declared_garmin_activity_id, status)
                        values (
                          @source_file_id, @owner_id, @stored_object_id, 'fit',
                          @original_name, @receipt_method, @garmin_activity_id, 'received');

                        insert into app.ingestion_runs (
                          id, owner_id, source_file_id, run_type, status,
                          tool_version, schema_version, sdk_version, correlation_id,
                          item_count, idempotency_key)
                        values (
                          @run_id, @owner_id, @source_file_id, 'fit_import', 'pending',
                          @tool_version, '1', @sdk_version, @correlation_id, 1,
                          @idempotency_key);

                        insert into app.ingestion_items (
                          owner_id, ingestion_run_id, ordinal, source_file_id,
                          observed_key, status, action)
                        values (
                          @owner_id, @run_id, 1, @source_file_id,
                          @garmin_activity_id::text, 'pending', 'awaiting_validation');

                        insert into app.audit_events (
                          owner_id, actor_id, actor_type, action, entity_type,
                          entity_id, correlation_id, changed_fields)
                        values (
                          @owner_id, @actor_id, @actor_type, 'fit_ingestion.enqueued',
                          'ingestion_run', @run_id, @correlation_id,
                          array['source_file_id', 'garmin_activity_id', 'status']);
                        """;
                    command.Parameters.AddWithValue("source_file_id", sourceFileId);
                    command.Parameters.AddWithValue("owner_id", ownerId);
                    command.Parameters.AddWithValue("stored_object_id", storedObjectId.Value);
                    command.Parameters.AddWithValue("original_name", safeName);
                    command.Parameters.AddWithValue("receipt_method", receiptMethod);
                    command.Parameters.AddWithValue("garmin_activity_id", garminActivityId.Value);
                    command.Parameters.AddWithValue("run_id", runId);
                    command.Parameters.AddWithValue("tool_version", $"running-performance-fit/{CanonicalFitProcessor.ProcessorVersion}");
                    command.Parameters.AddWithValue("sdk_version", CanonicalFitProcessor.SdkVersion);
                    command.Parameters.AddWithValue("correlation_id", httpContext.GetCorrelationId());
                    command.Parameters.Add("idempotency_key", NpgsqlDbType.Text).Value =
                        normalizedIdempotencyKey ?? (object)DBNull.Value;
                    command.Parameters.Add("actor_id", NpgsqlDbType.Uuid).Value =
                        syncClientId ?? ownerId;
                    command.Parameters.AddWithValue(
                        "actor_type",
                        syncClientId.HasValue ? "sync_client" : "athlete");
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
                await session.CommitAsync(cancellationToken);
                return Results.Accepted(
                    $"/api/v1/ingestion-runs/{runId}",
                    new FitImportAcceptedResponse(
                        runId,
                        sourceFileId,
                        staged.Sha256,
                        staged.SizeBytes,
                        garminActivityId.Value,
                        "pending",
                        !uploadedNewObject,
                        false));
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
                        // Storage inventory exposes an unreferenced object for operator cleanup.
                    }
                }
                throw;
            }
        }
    }

    private static async Task<IResult> ReprocessAsync(
        Guid id,
        HttpContext httpContext,
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        Guid sourceFileId;
        string sha256;
        long sizeBytes;
        long garminActivityId;
        await using (var lookup = session.Connection.CreateCommand())
        {
            lookup.Transaction = session.Transaction;
            lookup.CommandText = """
                select source.id, object.sha256, object.size_bytes,
                       source.declared_garmin_activity_id
                from app.ingestion_runs as run
                join app.source_files as source
                  on source.owner_id = run.owner_id and source.id = run.source_file_id
                join app.stored_objects as object
                  on object.owner_id = source.owner_id and object.id = source.stored_object_id
                where run.id = @run_id and source.source_kind = 'fit';
                """;
            lookup.Parameters.AddWithValue("run_id", id);
            await using var reader = await lookup.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return Results.NotFound();
            }
            sourceFileId = reader.GetGuid(0);
            sha256 = reader.GetString(1);
            sizeBytes = reader.GetInt64(2);
            garminActivityId = reader.GetInt64(3);
        }

        var runId = Guid.NewGuid();
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                insert into app.ingestion_runs (
                  id, owner_id, source_file_id, run_type, status, tool_version,
                  schema_version, sdk_version, correlation_id, item_count)
                values (
                  @new_run_id, @owner_id, @source_file_id, 'fit_reprocess', 'pending',
                  @tool_version, '1', @sdk_version, @correlation_id, 1);

                insert into app.ingestion_items (
                  owner_id, ingestion_run_id, ordinal, source_file_id,
                  observed_key, status, action)
                values (
                  @owner_id, @new_run_id, 1, @source_file_id,
                  @garmin_activity_id::text, 'pending', 'awaiting_reprocess');

                insert into app.audit_events (
                  owner_id, actor_id, actor_type, action, entity_type,
                  entity_id, correlation_id, changed_fields)
                values (
                  @owner_id, @owner_id, 'athlete', 'fit_ingestion.reprocess_enqueued',
                  'ingestion_run', @new_run_id, @correlation_id,
                  array['source_file_id', 'status']);
                """;
            command.Parameters.AddWithValue("new_run_id", runId);
            command.Parameters.AddWithValue("owner_id", ownerId);
            command.Parameters.AddWithValue("source_file_id", sourceFileId);
            command.Parameters.AddWithValue("tool_version", $"running-performance-fit/{CanonicalFitProcessor.ProcessorVersion}");
            command.Parameters.AddWithValue("sdk_version", CanonicalFitProcessor.SdkVersion);
            command.Parameters.AddWithValue("correlation_id", httpContext.GetCorrelationId());
            command.Parameters.AddWithValue("garmin_activity_id", garminActivityId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await session.CommitAsync(cancellationToken);
        return Results.Accepted(
            $"/api/v1/ingestion-runs/{runId}",
            new FitImportAcceptedResponse(
                runId,
                sourceFileId,
                sha256,
                sizeBytes,
                garminActivityId,
                "pending",
                true,
                false));
    }

    private static async Task<IResult> CreatePairingTokenAsync(
        CreatePairingTokenRequest request,
        ClaimsPrincipal principal,
        SyncCredentialService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.CreatePairingTokenAsync(
                principal.GetRequiredOwnerId(),
                request.DisplayName,
                cancellationToken);
            return Results.Ok(new PairingTokenResponse(
                result.PairingToken,
                result.DisplayName,
                result.ExpiresAt));
        }
        catch (ArgumentException exception)
        {
            return Problem(StatusCodes.Status400BadRequest, "sync_client_name_invalid", exception.Message);
        }
    }

    private static async Task<IResult> ExchangePairingTokenAsync(
        ExchangePairingTokenRequest request,
        SyncCredentialService service,
        CancellationToken cancellationToken)
    {
        var result = await service.ExchangePairingTokenAsync(
            request.PairingToken,
            cancellationToken);
        return result is null
            ? Problem(
                StatusCodes.Status400BadRequest,
                "sync_pairing_token_invalid",
                "The pairing token is invalid, expired, or already used.")
            : Results.Ok(new DeviceCredentialResponse(
                result.ClientId,
                result.Credential,
                result.ExpiresAt,
                result.Scopes));
    }

    private static async Task<IResult> ListSyncClientsAsync(
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        await using var session = await dataSource.OpenAsync(
            principal.GetRequiredOwnerId(),
            cancellationToken);
        var clients = new List<SyncClientResponse>();
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            select id, display_name, scopes, expires_at, revoked_at, last_used_at, created_at
            from app.sync_clients
            order by created_at desc, id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            clients.Add(new(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetFieldValue<string[]>(2),
                reader.GetDateTime(3),
                reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                reader.GetDateTime(6)));
        }
        await reader.DisposeAsync();
        await session.CommitAsync(cancellationToken);
        return Results.Ok(clients);
    }

    private static async Task<IResult> RevokeSyncClientAsync(
        Guid id,
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        await using var session = await dataSource.OpenAsync(
            principal.GetRequiredOwnerId(),
            cancellationToken);
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            update app.sync_clients
            set revoked_at = coalesce(revoked_at, now())
            where id = @id;
            """;
        command.Parameters.AddWithValue("id", id);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            return Results.NotFound();
        }
        await session.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<FitImportAcceptedResponse?> FindIdempotentRunAsync(
        Guid ownerId,
        string idempotencyKey,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            select run.id, source.id, object.sha256, object.size_bytes,
                   source.declared_garmin_activity_id, run.status
            from app.ingestion_runs as run
            join app.source_files as source
              on source.owner_id = run.owner_id and source.id = run.source_file_id
            join app.stored_objects as object
              on object.owner_id = source.owner_id and object.id = source.stored_object_id
            where run.run_type = 'fit_import' and run.idempotency_key = @idempotency_key;
            """;
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await reader.DisposeAsync();
            await session.CommitAsync(cancellationToken);
            return null;
        }
        var response = new FitImportAcceptedResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetString(5),
            true,
            true);
        await reader.DisposeAsync();
        await session.CommitAsync(cancellationToken);
        return response;
    }

    private static bool TryNormalizeIdempotencyKey(string? value, out string? normalized)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            normalized = null;
            return true;
        }
        normalized = value.Trim();
        if (normalized.Length <= 128
            && normalized.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '.' or '_' or ':' or '-'))
        {
            return true;
        }
        normalized = null;
        return false;
    }

    private static bool HasFitSignature(string path)
    {
        Span<byte> header = stackalloc byte[12];
        using var stream = File.OpenRead(path);
        return stream.Read(header) == header.Length
               && header[8] == (byte)'.'
               && header[9] == (byte)'F'
               && header[10] == (byte)'I'
               && header[11] == (byte)'T';
    }

    private static string SanitizeFitFileName(string fileName)
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
            sanitized = "activity.fit";
        }
        return sanitized.EndsWith(".fit", StringComparison.OrdinalIgnoreCase)
            ? sanitized
            : $"{sanitized}.fit";
    }

    private static IResult Problem(int status, string code, string detail) =>
        Results.Problem(
            statusCode: status,
            title: "FIT operation rejected",
            detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

public sealed record FitImportAcceptedResponse(
    Guid RunId,
    Guid SourceFileId,
    string Sha256,
    long SizeBytes,
    long GarminActivityId,
    string Status,
    bool ReusedStoredObject,
    bool ReusedReceipt);

public sealed record CreatePairingTokenRequest(string DisplayName);
public sealed record PairingTokenResponse(string PairingToken, string DisplayName, DateTime ExpiresAt);
public sealed record ExchangePairingTokenRequest(string PairingToken);
public sealed record DeviceCredentialResponse(
    Guid ClientId,
    string Credential,
    DateTime ExpiresAt,
    IReadOnlyList<string> Scopes);
public sealed record SyncClientResponse(
    Guid Id,
    string DisplayName,
    IReadOnlyList<string> Scopes,
    DateTime ExpiresAt,
    DateTime? RevokedAt,
    DateTime? LastUsedAt,
    DateTime CreatedAt);
