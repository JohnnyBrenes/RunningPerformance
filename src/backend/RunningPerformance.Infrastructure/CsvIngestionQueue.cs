using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using RunningPerformance.Application.Ingestion;
using RunningPerformance.Infrastructure.Database;
using RunningPerformance.Infrastructure.Storage;

namespace RunningPerformance.Infrastructure.Jobs;

public sealed class CsvIngestionQueue(
    NpgsqlDataSource dataSource,
    OwnerDataSource ownerDataSource,
    SupabaseObjectStore objectStore,
    NormalizedActivityCsvValidator validator,
    HistoricalImportOptions options,
    ILogger<CsvIngestionQueue> logger)
{
    private const string ToolVersion = "running-performance-csv/1.0.0";
    private static readonly string[] SourcedFields =
    [
        "garmin_activity_id",
        "activity_type",
        "activity_category",
        "modality",
        "started_at_local",
        "title",
        "distance_m",
        "duration_seconds",
        "moving_seconds",
        "elapsed_seconds",
        "average_pace_seconds_per_km",
        "average_speed_mps",
        "calories",
        "average_heart_rate_bpm",
        "max_heart_rate_bpm",
        "average_cadence_spm",
        "average_power_w",
        "elevation_gain_m",
        "lap_count"
    ];

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var claim = await ClaimNextAsync(cancellationToken);
        if (claim is null)
        {
            return false;
        }

        try
        {
            var source = await ReadSourceAsync(claim, cancellationToken);
            var temporaryPath = Path.Combine(
                Path.GetTempPath(),
                $"rp-csv-worker-{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var destination = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await objectStore.DownloadToAsync(source.ObjectPath, destination, cancellationToken);
                }

                var downloadedSize = new FileInfo(temporaryPath).Length;
                if (downloadedSize != source.SizeBytes || downloadedSize > options.MaxCsvBytes)
                {
                    throw new IngestionQuarantineException(
                        "csv_source_size_mismatch",
                        "The downloaded private source size does not match its receipt metadata.");
                }

                await using (var checksumInput = new FileStream(
                    temporaryPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var downloadedSha256 = Convert.ToHexString(
                            await SHA256.HashDataAsync(checksumInput, cancellationToken))
                        .ToLowerInvariant();
                    if (!string.Equals(downloadedSha256, source.Sha256, StringComparison.Ordinal))
                    {
                        throw new IngestionQuarantineException(
                            "csv_source_checksum_mismatch",
                            "The downloaded private source checksum does not match its receipt metadata.");
                    }
                }

                await HeartbeatAsync(claim, cancellationToken);
                await using var input = new FileStream(
                    temporaryPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var validation = await validator.ValidateAsync(input, cancellationToken);
                if (!validation.IsValid)
                {
                    await PersistValidationFailureAsync(claim, source, validation, cancellationToken);
                    logger.LogWarning(
                        "CSV ingestion run {RunId} failed contract validation with {ErrorCount} row errors.",
                        claim.RunId,
                        validation.Errors.Count);
                    return true;
                }

                await PublishAsync(claim, source, validation.Rows, cancellationToken);
                logger.LogInformation(
                    "CSV ingestion run {RunId} reconciled {ItemCount} activities.",
                    claim.RunId,
                    validation.Rows.Count);
                return true;
            }
            finally
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    logger.LogWarning("Temporary CSV cleanup failed for run {RunId}.", claim.RunId);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IngestionQuarantineException exception)
        {
            await QuarantineAsync(claim, exception.Code, exception.Message, cancellationToken);
            logger.LogWarning(
                "CSV ingestion run {RunId} was quarantined with code {ErrorCode}.",
                claim.RunId,
                exception.Code);
            return true;
        }
        catch (Exception exception)
        {
            var code = exception is ObjectStoreException storageException
                ? storageException.Code
                : "csv_processing_failed";
            await RetryOrFailAsync(claim, code, cancellationToken);
            logger.LogError(
                "CSV ingestion run {RunId} attempt {AttemptCount} failed with code {ErrorCode}.",
                claim.RunId,
                claim.AttemptCount,
                code);
            return true;
        }
    }

    private async Task<ClaimedRun?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await OwnerTransactionContext.ApplyWorkerCoordinatorAsync(
            connection,
            transaction,
            cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select id, owner_id, source_file_id, correlation_id, attempt_count
            from app.claim_csv_ingestion_run(@lease_owner, @lease_seconds);
            """;
        command.Parameters.AddWithValue("lease_owner", Environment.MachineName);
        command.Parameters.AddWithValue("lease_seconds", options.LeaseSeconds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        if (reader.IsDBNull(2))
        {
            throw new InvalidOperationException("A CSV ingestion run has no source file.");
        }

        var claim = new ClaimedRun(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetInt32(4),
            Environment.MachineName);
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return claim;
    }

    private async Task<SourceDescriptor> ReadSourceAsync(
        ClaimedRun claim,
        CancellationToken cancellationToken)
    {
        await using var session = await ownerDataSource.OpenWorkerAsync(claim.OwnerId, cancellationToken);
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            select
              source.id, source.original_name, object.id, object.object_path,
              object.sha256, object.size_bytes
            from app.source_files as source
            join app.stored_objects as object
              on object.owner_id = source.owner_id
             and object.id = source.stored_object_id
            where source.id = @source_file_id
              and source.source_kind = 'normalized_csv';
            """;
        command.Parameters.AddWithValue("source_file_id", claim.SourceFileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new IngestionQuarantineException(
                "csv_source_missing",
                "The private normalized CSV source is unavailable.");
        }

        var descriptor = new SourceDescriptor(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt64(5));
        await reader.DisposeAsync();
        await session.CommitAsync(cancellationToken);
        return descriptor;
    }

    private async Task PersistValidationFailureAsync(
        ClaimedRun claim,
        SourceDescriptor source,
        NormalizedCsvValidationResult validation,
        CancellationToken cancellationToken)
    {
        await using var session = await ownerDataSource.OpenWorkerAsync(claim.OwnerId, cancellationToken);
        await DeleteEnvelopeItemAsync(session, claim.RunId, cancellationToken);

        var errorsByOrdinal = validation.Errors
            .GroupBy(error => error.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var rowsByOrdinal = validation.Rows.ToDictionary(row => row.Ordinal);
        var itemCount = Math.Max(1, validation.ObservedRowCount);

        for (var ordinal = 1; ordinal <= itemCount; ordinal++)
        {
            errorsByOrdinal.TryGetValue(ordinal, out var errors);
            rowsByOrdinal.TryGetValue(ordinal, out var row);
            await using var command = session.Connection.CreateCommand();
            command.Transaction = session.Transaction;
            command.CommandText = """
                insert into app.ingestion_items (
                  owner_id, ingestion_run_id, ordinal, source_file_id, observed_key,
                  status, action, error_code, error_message)
                values (
                  @owner_id, @run_id, @ordinal, @source_file_id, @observed_key,
                  @status, @action, @error_code, @error_message);
                """;
            command.Parameters.AddWithValue("owner_id", claim.OwnerId);
            command.Parameters.AddWithValue("run_id", claim.RunId);
            command.Parameters.AddWithValue("ordinal", ordinal);
            command.Parameters.AddWithValue("source_file_id", source.SourceFileId);
            AddNullable(command, "observed_key", NpgsqlDbType.Text, row?.ProvisionalActivityKey ?? errors?.FirstOrDefault()?.ObservedKey);
            command.Parameters.AddWithValue("status", errors is null ? "validated" : "failed");
            command.Parameters.AddWithValue("action", errors is null ? "not_published" : "rejected");
            AddNullable(command, "error_code", NpgsqlDbType.Text, errors?.First().Code);
            AddNullable(
                command,
                "error_message",
                NpgsqlDbType.Text,
                errors is null ? null : string.Join("; ", errors.Select(error => error.Message)));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                update app.source_files
                set status = 'rejected'
                where id = @source_file_id;

                update app.ingestion_runs
                set status = 'failed',
                    finished_at = now(),
                    item_count = @item_count,
                    success_count = 0,
                    failure_count = @failure_count,
                    lease_owner = null,
                    lease_until = null,
                    heartbeat_at = now()
                where id = @run_id;
                """;
            command.Parameters.AddWithValue("source_file_id", source.SourceFileId);
            command.Parameters.AddWithValue("run_id", claim.RunId);
            command.Parameters.AddWithValue("item_count", itemCount);
            command.Parameters.AddWithValue("failure_count", validation.Errors.Select(error => error.Ordinal).Distinct().Count());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
            session,
            claim,
            "historical_csv.rejected",
            ["status", "item_count", "failure_count"],
            cancellationToken);
        await session.CommitAsync(cancellationToken);
    }

    private async Task PublishAsync(
        ClaimedRun claim,
        SourceDescriptor source,
        IReadOnlyList<NormalizedActivityRow> rows,
        CancellationToken cancellationToken)
    {
        await using var session = await ownerDataSource.OpenWorkerAsync(claim.OwnerId, cancellationToken);
        await DeleteEnvelopeItemAsync(session, claim.RunId, cancellationToken);

        foreach (var row in rows)
        {
            if (row.Ordinal % 50 == 0)
            {
                await HeartbeatAsync(claim, cancellationToken);
            }

            var itemId = Guid.NewGuid();
            await InsertItemAsync(session, claim, source, row, itemId, cancellationToken);
            var (activityId, action) = await ReconcileActivityAsync(
                session,
                claim.OwnerId,
                row,
                cancellationToken);
            var observationId = await InsertObservationAsync(
                session,
                claim,
                source,
                row,
                itemId,
                activityId,
                cancellationToken);
            await SelectFieldSourcesAsync(
                session,
                activityId,
                observationId,
                cancellationToken);
            await ApplyItemAsync(session, itemId, activityId, action, cancellationToken);
        }

        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                update app.source_files
                set status = 'accepted'
                where id = @source_file_id;

                update app.stored_objects
                set accepted_at = coalesce(accepted_at, now())
                where id = @stored_object_id;

                update app.ingestion_runs
                set status = 'succeeded',
                    tool_version = @tool_version,
                    schema_version = @schema_version,
                    finished_at = now(),
                    item_count = @item_count,
                    success_count = @item_count,
                    failure_count = 0,
                    lease_owner = null,
                    lease_until = null,
                    heartbeat_at = now()
                where id = @run_id;
                """;
            command.Parameters.AddWithValue("source_file_id", source.SourceFileId);
            command.Parameters.AddWithValue("stored_object_id", source.StoredObjectId);
            command.Parameters.AddWithValue("run_id", claim.RunId);
            command.Parameters.AddWithValue("tool_version", ToolVersion);
            command.Parameters.AddWithValue("schema_version", NormalizedActivityCsvContract.SchemaVersion);
            command.Parameters.AddWithValue("item_count", rows.Count);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
            session,
            claim,
            "historical_csv.published",
            ["status", "item_count", "success_count"],
            cancellationToken);
        await session.CommitAsync(cancellationToken);
    }

    private static async Task DeleteEnvelopeItemAsync(
        OwnerDbSession session,
        Guid runId,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = "delete from app.ingestion_items where ingestion_run_id = @run_id;";
        command.Parameters.AddWithValue("run_id", runId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertItemAsync(
        OwnerDbSession session,
        ClaimedRun claim,
        SourceDescriptor source,
        NormalizedActivityRow row,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            insert into app.ingestion_items (
              id, owner_id, ingestion_run_id, ordinal, source_file_id,
              observed_key, status, action)
            values (
              @id, @owner_id, @run_id, @ordinal, @source_file_id,
              @observed_key, 'validated', 'pending_publication');
            """;
        command.Parameters.AddWithValue("id", itemId);
        command.Parameters.AddWithValue("owner_id", claim.OwnerId);
        command.Parameters.AddWithValue("run_id", claim.RunId);
        command.Parameters.AddWithValue("ordinal", row.Ordinal);
        command.Parameters.AddWithValue("source_file_id", source.SourceFileId);
        command.Parameters.AddWithValue("observed_key", row.ProvisionalActivityKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(Guid ActivityId, string Action)> ReconcileActivityAsync(
        OwnerDbSession session,
        Guid ownerId,
        NormalizedActivityRow row,
        CancellationToken cancellationToken)
    {
        Guid? existingId = null;
        long? existingGarminId = null;
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select id, garmin_activity_id
                from app.activities
                where provisional_activity_key = @provisional_key;
                """;
            command.Parameters.AddWithValue("provisional_key", row.ProvisionalActivityKey);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                existingId = reader.GetGuid(0);
                existingGarminId = reader.IsDBNull(1) ? null : reader.GetInt64(1);
            }
        }

        if (existingGarminId.HasValue
            && row.GarminActivityId.HasValue
            && existingGarminId != row.GarminActivityId)
        {
            throw new IngestionQuarantineException(
                "garmin_id_conflict",
                "An existing provisional activity has a different Garmin activity ID.");
        }

        if (row.GarminActivityId.HasValue)
        {
            await using var command = session.Connection.CreateCommand();
            command.Transaction = session.Transaction;
            command.CommandText = """
                select id
                from app.activities
                where garmin_activity_id = @garmin_activity_id;
                """;
            command.Parameters.AddWithValue("garmin_activity_id", row.GarminActivityId.Value);
            var garminOwner = await command.ExecuteScalarAsync(cancellationToken);
            if (garminOwner is Guid garminActivityId
                && garminActivityId != existingId)
            {
                throw new IngestionQuarantineException(
                    "garmin_id_collision",
                    "A Garmin activity ID is already linked to another provisional activity.");
            }
        }

        var activityId = existingId ?? Guid.NewGuid();
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = existingId.HasValue
                ? """
                    update app.activities
                    set garmin_activity_id = coalesce(garmin_activity_id, @garmin_activity_id),
                        activity_type = @activity_type,
                        activity_category = @activity_category,
                        modality = @modality,
                        started_at_local = @started_at_local,
                        title = @title,
                        distance_m = @distance_m,
                        duration_seconds = @duration_seconds,
                        moving_seconds = @moving_seconds,
                        elapsed_seconds = @elapsed_seconds,
                        average_pace_seconds_per_km = @average_pace,
                        average_speed_mps = @average_speed,
                        calories = @calories,
                        average_heart_rate_bpm = @average_hr,
                        max_heart_rate_bpm = @max_hr,
                        average_cadence_spm = @average_cadence,
                        average_power_w = @average_power,
                        elevation_gain_m = @elevation_gain,
                        lap_count = @lap_count,
                        validation_status = 'published'
                    where id = @activity_id;
                    """
                : """
                    insert into app.activities (
                      id, owner_id, provisional_activity_key, garmin_activity_id,
                      activity_type, activity_category, modality, started_at_local, title,
                      distance_m, duration_seconds, moving_seconds, elapsed_seconds,
                      average_pace_seconds_per_km, average_speed_mps, calories,
                      average_heart_rate_bpm, max_heart_rate_bpm, average_cadence_spm,
                      average_power_w, elevation_gain_m, lap_count, validation_status)
                    values (
                      @activity_id, @owner_id, @provisional_key, @garmin_activity_id,
                      @activity_type, @activity_category, @modality, @started_at_local, @title,
                      @distance_m, @duration_seconds, @moving_seconds, @elapsed_seconds,
                      @average_pace, @average_speed, @calories,
                      @average_hr, @max_hr, @average_cadence,
                      @average_power, @elevation_gain, @lap_count, 'published');
                    """;
            command.Parameters.AddWithValue("activity_id", activityId);
            command.Parameters.AddWithValue("owner_id", ownerId);
            command.Parameters.AddWithValue("provisional_key", row.ProvisionalActivityKey);
            AddNullable(command, "garmin_activity_id", NpgsqlDbType.Bigint, row.GarminActivityId);
            command.Parameters.AddWithValue("activity_type", row.ActivityType);
            command.Parameters.AddWithValue("activity_category", row.ActivityCategory);
            command.Parameters.AddWithValue("modality", row.Modality);
            command.Parameters.AddWithValue("started_at_local", NpgsqlDbType.Timestamp, row.StartedAtLocal);
            AddNullable(command, "title", NpgsqlDbType.Text, row.Title);
            AddNullable(command, "distance_m", NpgsqlDbType.Numeric, row.DistanceM);
            AddNullable(command, "duration_seconds", NpgsqlDbType.Numeric, row.DurationSeconds);
            AddNullable(command, "moving_seconds", NpgsqlDbType.Numeric, row.MovingSeconds);
            AddNullable(command, "elapsed_seconds", NpgsqlDbType.Numeric, row.ElapsedSeconds);
            AddNullable(command, "average_pace", NpgsqlDbType.Numeric, row.AveragePaceSecondsPerKm);
            AddNullable(command, "average_speed", NpgsqlDbType.Numeric, row.AverageSpeedMps);
            AddNullable(command, "calories", NpgsqlDbType.Numeric, row.Calories);
            AddNullable(command, "average_hr", NpgsqlDbType.Numeric, row.AverageHeartRateBpm);
            AddNullable(command, "max_hr", NpgsqlDbType.Numeric, row.MaxHeartRateBpm);
            AddNullable(command, "average_cadence", NpgsqlDbType.Numeric, row.AverageCadenceSpm);
            AddNullable(command, "average_power", NpgsqlDbType.Numeric, row.AveragePowerW);
            AddNullable(command, "elevation_gain", NpgsqlDbType.Numeric, row.ElevationGainM);
            AddNullable(command, "lap_count", NpgsqlDbType.Integer, row.LapCount);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return (activityId, existingId.HasValue ? "reconciled" : "inserted");
    }

    private static async Task<Guid> InsertObservationAsync(
        OwnerDbSession session,
        ClaimedRun claim,
        SourceDescriptor source,
        NormalizedActivityRow row,
        Guid itemId,
        Guid activityId,
        CancellationToken cancellationToken)
    {
        var observationId = Guid.NewGuid();
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            insert into app.activity_source_observations (
              id, owner_id, activity_id, source_file_id, ingestion_item_id,
              source_class, source_row_number, observed_keys, summary_payload,
              linking_result, observed_at)
            values (
              @id, @owner_id, @activity_id, @source_file_id, @ingestion_item_id,
              'normalized_csv_row', @source_row_number, @observed_keys, @summary_payload,
              'provisional_key_exact', @observed_at);
            """;
        command.Parameters.AddWithValue("id", observationId);
        command.Parameters.AddWithValue("owner_id", claim.OwnerId);
        command.Parameters.AddWithValue("activity_id", activityId);
        command.Parameters.AddWithValue("source_file_id", source.SourceFileId);
        command.Parameters.AddWithValue("ingestion_item_id", itemId);
        command.Parameters.AddWithValue("source_row_number", row.SourceRowNumber);
        command.Parameters.AddWithValue(
            "observed_keys",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(new
            {
                row.ProvisionalActivityKey,
                row.GarminActivityId
            }));
        command.Parameters.AddWithValue(
            "summary_payload",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(row.SourceValues));
        command.Parameters.AddWithValue("observed_at", NpgsqlDbType.TimestampTz, DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return observationId;
    }

    private static async Task SelectFieldSourcesAsync(
        OwnerDbSession session,
        Guid activityId,
        Guid observationId,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            insert into app.activity_field_sources (
              owner_id, activity_id, field_name, source_observation_id, precedence_rule)
            select app.current_owner_id(), @activity_id, field_name, @observation_id,
                   'normalized_csv_latest_reconciled'
            from unnest(@field_names::text[]) as field_name
            on conflict (activity_id, field_name) do update
            set source_observation_id = excluded.source_observation_id,
                precedence_rule = excluded.precedence_rule,
                selected_at = now();
            """;
        command.Parameters.AddWithValue("activity_id", activityId);
        command.Parameters.AddWithValue("observation_id", observationId);
        command.Parameters.AddWithValue("field_names", SourcedFields);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ApplyItemAsync(
        OwnerDbSession session,
        Guid itemId,
        Guid activityId,
        string action,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            update app.ingestion_items
            set target_activity_id = @activity_id,
                status = 'applied',
                action = @action
            where id = @item_id;
            """;
        command.Parameters.AddWithValue("activity_id", activityId);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("item_id", itemId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task HeartbeatAsync(ClaimedRun claim, CancellationToken cancellationToken)
    {
        await using var session = await ownerDataSource.OpenWorkerAsync(claim.OwnerId, cancellationToken);
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            update app.ingestion_runs
            set heartbeat_at = now(),
                lease_until = now() + make_interval(secs => @lease_seconds)
            where id = @run_id
              and status = 'running'
              and lease_owner = @lease_owner;
            """;
        command.Parameters.AddWithValue("lease_seconds", options.LeaseSeconds);
        command.Parameters.AddWithValue("run_id", claim.RunId);
        command.Parameters.AddWithValue("lease_owner", claim.LeaseOwner);
        var updated = await command.ExecuteNonQueryAsync(cancellationToken);
        if (updated != 1)
        {
            throw new InvalidOperationException("The CSV ingestion lease was lost.");
        }

        await session.CommitAsync(cancellationToken);
    }

    private async Task QuarantineAsync(
        ClaimedRun claim,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        await using var session = await ownerDataSource.OpenWorkerAsync(claim.OwnerId, cancellationToken);
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            update app.ingestion_runs
            set status = 'quarantined',
                finished_at = now(),
                failure_count = greatest(failure_count, 1),
                lease_owner = null,
                lease_until = null,
                heartbeat_at = now()
            where id = @run_id;

            update app.source_files
            set status = 'quarantined'
            where id = @source_file_id;

            update app.ingestion_items
            set status = 'quarantined',
                action = 'quarantined',
                error_code = @error_code,
                error_message = @error_message,
                retryable = false
            where ingestion_run_id = @run_id;
            """;
        command.Parameters.AddWithValue("run_id", claim.RunId);
        command.Parameters.AddWithValue("source_file_id", claim.SourceFileId);
        command.Parameters.AddWithValue("error_code", code);
        command.Parameters.AddWithValue("error_message", message);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await session.CommitAsync(cancellationToken);
    }

    private async Task RetryOrFailAsync(
        ClaimedRun claim,
        string code,
        CancellationToken cancellationToken)
    {
        var terminal = claim.AttemptCount >= options.MaxAttempts;
        var delaySeconds = Math.Min(300, 5 * (1 << Math.Min(6, claim.AttemptCount - 1)));
        await using var session = await ownerDataSource.OpenWorkerAsync(claim.OwnerId, cancellationToken);
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            update app.ingestion_runs
            set status = @status,
                finished_at = case when @terminal then now() else null end,
                next_attempt_at = case when @terminal then null else now() + make_interval(secs => @delay_seconds) end,
                failure_count = case when @terminal then greatest(failure_count, 1) else failure_count end,
                lease_owner = null,
                lease_until = null,
                heartbeat_at = now()
            where id = @run_id;

            update app.ingestion_items
            set status = case when @terminal then 'failed' else 'pending' end,
                action = case when @terminal then 'failed' else 'retry_scheduled' end,
                error_code = @error_code,
                error_message = 'The ingestion attempt failed; inspect the sanitized error code.',
                retryable = not @terminal
            where ingestion_run_id = @run_id;

            update app.source_files
            set status = case when @terminal then 'rejected' else status end
            where id = @source_file_id;
            """;
        command.Parameters.AddWithValue("status", terminal ? "failed" : "pending");
        command.Parameters.AddWithValue("terminal", terminal);
        command.Parameters.AddWithValue("delay_seconds", delaySeconds);
        command.Parameters.AddWithValue("run_id", claim.RunId);
        command.Parameters.AddWithValue("source_file_id", claim.SourceFileId);
        command.Parameters.AddWithValue("error_code", code);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await session.CommitAsync(cancellationToken);
    }

    private static async Task WriteAuditAsync(
        OwnerDbSession session,
        ClaimedRun claim,
        string action,
        string[] changedFields,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            insert into app.audit_events (
              owner_id, actor_type, action, entity_type, entity_id,
              correlation_id, changed_fields)
            values (
              @owner_id, 'worker', @action, 'ingestion_run', @run_id,
              @correlation_id, @changed_fields);
            """;
        command.Parameters.AddWithValue("owner_id", claim.OwnerId);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("run_id", claim.RunId);
        command.Parameters.AddWithValue("correlation_id", claim.CorrelationId);
        command.Parameters.AddWithValue("changed_fields", changedFields);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddNullable(
        NpgsqlCommand command,
        string name,
        NpgsqlDbType type,
        object? value)
    {
        command.Parameters.Add(name, type).Value = value ?? DBNull.Value;
    }

    private sealed record ClaimedRun(
        Guid RunId,
        Guid OwnerId,
        Guid SourceFileId,
        Guid CorrelationId,
        int AttemptCount,
        string LeaseOwner);

    private sealed record SourceDescriptor(
        Guid SourceFileId,
        string OriginalName,
        Guid StoredObjectId,
        string ObjectPath,
        string Sha256,
        long SizeBytes);

    private sealed class IngestionQuarantineException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
