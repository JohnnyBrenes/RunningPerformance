using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using RunningPerformance.Application.Ingestion;
using RunningPerformance.Fit;
using RunningPerformance.Infrastructure.Database;
using RunningPerformance.Infrastructure.Storage;

namespace RunningPerformance.Infrastructure.Jobs;

public sealed class FitIngestionQueue(
    NpgsqlDataSource dataSource,
    OwnerDataSource ownerDataSource,
    SupabaseObjectStore objectStore,
    FitIngestionOptions options,
    ILogger<FitIngestionQueue> logger)
{
    private static readonly string[] SummaryFields =
    [
        "activity_type", "activity_category", "modality", "started_at_local",
        "started_at_utc", "title", "distance_m", "duration_seconds",
        "elapsed_seconds", "average_pace_seconds_per_km", "average_speed_mps",
        "calories", "average_heart_rate_bpm", "max_heart_rate_bpm",
        "average_cadence_spm", "average_power_w", "elevation_gain_m", "lap_count"
    ];

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var claim = await ClaimNextAsync(cancellationToken);
        if (claim is null)
        {
            return false;
        }

        FitActivityData? parsed = null;
        try
        {
            var source = await ReadSourceAsync(claim, cancellationToken);
            if (source.GarminActivityId is null)
            {
                throw new FitQuarantineException(
                    "fit_garmin_id_missing",
                    "The FIT receipt has no Garmin activity ID.");
            }

            var temporaryPath = Path.Combine(
                Path.GetTempPath(),
                $"rp-fit-worker-{Guid.NewGuid():N}.fit");
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
                    await objectStore.DownloadToAsync(
                        source.ObjectPath,
                        destination,
                        cancellationToken);
                }

                await ValidateDownloadedSourceAsync(
                    temporaryPath,
                    source,
                    cancellationToken);
                await HeartbeatAsync(claim, cancellationToken);

                var canonical = CanonicalFitProcessor.Process(
                    source.GarminActivityId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    temporaryPath);
                parsed = FitActivityNormalizer.Normalize(canonical);
                await PublishAsync(claim, source, parsed, cancellationToken);
                logger.LogInformation(
                    "FIT ingestion run {RunId} published {RecordCount} samples.",
                    claim.RunId,
                    parsed.Samples.Count);
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
                    logger.LogWarning(
                        "Temporary FIT cleanup failed for run {RunId}.",
                        claim.RunId);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FitQuarantineException exception)
        {
            await QuarantineAsync(
                claim,
                exception.Code,
                exception.Message,
                parsed,
                cancellationToken);
            logger.LogWarning(
                "FIT ingestion run {RunId} was quarantined with code {ErrorCode}.",
                claim.RunId,
                exception.Code);
            return true;
        }
        catch (InvalidDataException exception)
        {
            await QuarantineAsync(
                claim,
                "fit_validation_failed",
                "The FIT failed structural, CRC, SDK, or semantic validation.",
                parsed,
                cancellationToken);
            logger.LogWarning(
                exception,
                "FIT ingestion run {RunId} failed validation.",
                claim.RunId);
            return true;
        }
        catch (Exception exception)
        {
            var code = exception is ObjectStoreException storageException
                ? storageException.Code
                : "fit_processing_failed";
            await RetryOrFailAsync(claim, code, cancellationToken);
            logger.LogError(
                exception,
                "FIT ingestion run {RunId} attempt {AttemptCount} failed with code {ErrorCode}.",
                claim.RunId,
                claim.AttemptCount,
                code);
            return true;
        }
    }

    private async Task<ClaimedFitRun?> ClaimNextAsync(CancellationToken cancellationToken)
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
            select id, owner_id, source_file_id, correlation_id, attempt_count, run_type
            from app.claim_fit_ingestion_run(@lease_owner, @lease_seconds);
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

        var result = new ClaimedFitRun(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetInt32(4),
            reader.GetString(5),
            Environment.MachineName);
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<FitSourceDescriptor> ReadSourceAsync(
        ClaimedFitRun claim,
        CancellationToken cancellationToken)
    {
        await using var session = await ownerDataSource.OpenWorkerAsync(
            claim.OwnerId,
            cancellationToken);
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            select source.id, source.original_name, source.declared_garmin_activity_id,
                   object.object_path, object.sha256, object.size_bytes
            from app.source_files as source
            join app.stored_objects as object
              on object.owner_id = source.owner_id
             and object.id = source.stored_object_id
            where source.id = @source_file_id
              and source.source_kind = 'fit';
            """;
        command.Parameters.AddWithValue("source_file_id", claim.SourceFileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The queued FIT source no longer exists.");
        }
        var result = new FitSourceDescriptor(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetInt64(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt64(5));
        await reader.DisposeAsync();
        await session.CommitAsync(cancellationToken);
        return result;
    }

    private async Task ValidateDownloadedSourceAsync(
        string path,
        FitSourceDescriptor source,
        CancellationToken cancellationToken)
    {
        var size = new FileInfo(path).Length;
        if (size != source.SizeBytes || size > options.MaxFitBytes)
        {
            throw new FitQuarantineException(
                "fit_source_size_mismatch",
                "The private FIT size does not match its receipt metadata.");
        }
        await using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var sha256 = Convert.ToHexString(
                await SHA256.HashDataAsync(input, cancellationToken))
            .ToLowerInvariant();
        if (!string.Equals(sha256, source.Sha256, StringComparison.Ordinal))
        {
            throw new FitQuarantineException(
                "fit_source_checksum_mismatch",
                "The private FIT checksum does not match its receipt metadata.");
        }
    }

    private async Task PublishAsync(
        ClaimedFitRun claim,
        FitSourceDescriptor source,
        FitActivityData data,
        CancellationToken cancellationToken)
    {
        await using var session = await ownerDataSource.OpenWorkerAsync(
            claim.OwnerId,
            cancellationToken);
        var identity = await ResolveIdentityAsync(
            session,
            claim,
            source,
            data,
            cancellationToken);
        if (identity.Skip)
        {
            await CompleteSkippedAsync(
                session,
                claim,
                source,
                identity.ActivityId,
                cancellationToken);
            await session.CommitAsync(cancellationToken);
            return;
        }

        var activityId = identity.ActivityId;
        var attemptId = Guid.NewGuid();
        await RemoveCurrentDerivedDataAsync(session, activityId, cancellationToken);
        await InsertAttemptAsync(
            session,
            claim,
            source,
            data,
            attemptId,
            cancellationToken);
        await UpsertActivitySummaryAsync(
            session,
            claim.OwnerId,
            source,
            data.Summary,
            activityId,
            identity.Created,
            cancellationToken);
        var observationId = await InsertObservationAsync(
            session,
            claim,
            source,
            data,
            activityId,
            cancellationToken);
        await SelectFieldSourcesAsync(
            session,
            data.Summary,
            activityId,
            observationId,
            identity.Created,
            cancellationToken);
        await InsertNormalizedDetailAsync(
            session,
            claim.OwnerId,
            activityId,
            attemptId,
            data,
            cancellationToken);
        await CompleteAppliedAsync(
            session,
            claim,
            source,
            activityId,
            identity.Created ? "inserted" : "enriched",
            cancellationToken);
        await session.CommitAsync(cancellationToken);
    }

    private async Task<ResolvedIdentity> ResolveIdentityAsync(
        OwnerDbSession session,
        ClaimedFitRun claim,
        FitSourceDescriptor source,
        FitActivityData data,
        CancellationToken cancellationToken)
    {
        if (await ExistsAsync(
                session,
                """
                select exists (
                  select 1
                  from app.source_files as other_source
                  join app.stored_objects as other_object
                    on other_object.owner_id = other_source.owner_id
                   and other_object.id = other_source.stored_object_id
                  where other_source.source_kind = 'fit'
                    and other_source.id <> @source_file_id
                    and other_object.sha256 = @sha256
                    and other_source.declared_garmin_activity_id is not null
                    and other_source.declared_garmin_activity_id <> @garmin_activity_id
                );
                """,
                source,
                cancellationToken))
        {
            throw new FitQuarantineException(
                "fit_hash_multiple_garmin_ids",
                "The same FIT hash was received with different Garmin activity IDs.");
        }
        if (await ExistsAsync(
                session,
                """
                select exists (
                  select 1
                  from app.source_files as other_source
                  join app.stored_objects as other_object
                    on other_object.owner_id = other_source.owner_id
                   and other_object.id = other_source.stored_object_id
                  where other_source.source_kind = 'fit'
                    and other_source.id <> @source_file_id
                    and other_source.declared_garmin_activity_id = @garmin_activity_id
                    and other_object.sha256 <> @sha256
                );
                """,
                source,
                cancellationToken))
        {
            throw new FitQuarantineException(
                "fit_garmin_id_hash_collision",
                "The Garmin activity ID already exists with different FIT content.");
        }

        Guid? activityId = null;
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select id
                from app.activities
                where garmin_activity_id = @garmin_activity_id;
                """;
            command.Parameters.AddWithValue(
                "garmin_activity_id",
                source.GarminActivityId!.Value);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            activityId = value is Guid id ? id : null;
        }

        if (activityId.HasValue && claim.RunType == "fit_import")
        {
            await using var command = session.Connection.CreateCommand();
            command.Transaction = session.Transaction;
            command.CommandText = """
                select exists (
                  select 1
                  from app.fit_processing_attempts as attempt
                  join app.source_files as attempt_source
                    on attempt_source.owner_id = attempt.owner_id
                   and attempt_source.id = attempt.source_file_id
                  join app.stored_objects as attempt_object
                    on attempt_object.owner_id = attempt_source.owner_id
                   and attempt_object.id = attempt_source.stored_object_id
                  where attempt.is_current
                    and attempt.status = 'validated'
                    and attempt_object.sha256 = @sha256
                    and exists (
                      select 1 from app.activity_fit_sessions as fit_session
                      where fit_session.fit_processing_attempt_id = attempt.id
                        and fit_session.activity_id = @activity_id
                    )
                );
                """;
            command.Parameters.AddWithValue("sha256", source.Sha256);
            command.Parameters.AddWithValue("activity_id", activityId.Value);
            if (Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken)))
            {
                return new(activityId.Value, Created: false, Skip: true);
            }
        }

        if (activityId.HasValue)
        {
            return new(activityId.Value, Created: false, Skip: false);
        }

        var candidates = new List<Guid>();
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select id
                from app.activities
                where garmin_activity_id is null
                  and started_at_local = @started_at_local
                  and activity_category = @activity_category
                  and @duration_seconds::numeric is not null
                  and duration_seconds is not null
                  and abs(duration_seconds - @duration_seconds) <= 2
                  and @distance_m::numeric is not null
                  and distance_m is not null
                  and abs(distance_m - @distance_m)
                      <= greatest(20::numeric, @distance_m * 0.002)
                order by id;
                """;
            command.Parameters.AddWithValue(
                "started_at_local",
                NpgsqlDbType.Timestamp,
                data.Summary.StartedAtLocal!.Value);
            command.Parameters.AddWithValue(
                "activity_category",
                data.Summary.ActivityCategory);
            AddNullable(
                command,
                "duration_seconds",
                NpgsqlDbType.Numeric,
                data.Summary.DurationSeconds);
            AddNullable(command, "distance_m", NpgsqlDbType.Numeric, data.Summary.DistanceM);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add(reader.GetGuid(0));
            }
        }
        if (candidates.Count > 1)
        {
            throw new FitQuarantineException(
                "fit_historical_link_ambiguous",
                "More than one historical activity satisfies the strict FIT linking contract.");
        }
        if (candidates.Count == 1)
        {
            return new(candidates[0], Created: false, Skip: false);
        }
        return new(Guid.NewGuid(), Created: true, Skip: false);
    }

    private static async Task<bool> ExistsAsync(
        OwnerDbSession session,
        string sql,
        FitSourceDescriptor source,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("source_file_id", source.SourceFileId);
        command.Parameters.AddWithValue("sha256", source.Sha256);
        command.Parameters.AddWithValue("garmin_activity_id", source.GarminActivityId!.Value);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task RemoveCurrentDerivedDataAsync(
        OwnerDbSession session,
        Guid activityId,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            delete from app.activity_samples where activity_id = @activity_id;
            delete from app.activity_events where activity_id = @activity_id;
            delete from app.activity_time_in_zones where activity_id = @activity_id;
            delete from app.activity_laps where activity_id = @activity_id;
            delete from app.activity_fit_sessions where activity_id = @activity_id;
            update app.fit_processing_attempts
            set is_current = false
            where is_current
              and id in (
                select attempt.id
                from app.fit_processing_attempts as attempt
                where attempt.owner_id = app.current_owner_id()
                  and exists (
                    select 1
                    from app.activity_source_observations as observation
                    where observation.activity_id = @activity_id
                      and observation.source_file_id = attempt.source_file_id
                  )
              );
            """;
        command.Parameters.AddWithValue("activity_id", activityId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAttemptAsync(
        OwnerDbSession session,
        ClaimedFitRun claim,
        FitSourceDescriptor source,
        FitActivityData data,
        Guid attemptId,
        CancellationToken cancellationToken)
    {
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                insert into app.fit_processing_attempts (
                  id, owner_id, source_file_id, ingestion_run_id,
                  processor_version, sdk_version, schema_version,
                  signature_valid, declared_size_valid, crc_valid, full_read_valid,
                  sha256, message_count, record_count, status, is_current)
                values (
                  @id, @owner_id, @source_file_id, @run_id,
                  @processor_version, @sdk_version, @schema_version,
                  @signature_valid, true, @crc_valid, @full_read_valid,
                  @sha256, @message_count, @record_count, 'validated', true);
                """;
            command.Parameters.AddWithValue("id", attemptId);
            command.Parameters.AddWithValue("owner_id", claim.OwnerId);
            command.Parameters.AddWithValue("source_file_id", source.SourceFileId);
            command.Parameters.AddWithValue("run_id", claim.RunId);
            command.Parameters.AddWithValue(
                "processor_version",
                data.Canonical.Canonicalizer.Version);
            command.Parameters.AddWithValue("sdk_version", data.Canonical.Canonicalizer.DecoderVersion);
            command.Parameters.AddWithValue(
                "schema_version",
                data.Canonical.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("signature_valid", data.Canonical.Validation.IsFit);
            command.Parameters.AddWithValue("crc_valid", data.Canonical.Validation.IntegrityValid);
            command.Parameters.AddWithValue("full_read_valid", data.Canonical.Validation.ReadSuccessful);
            command.Parameters.AddWithValue("sha256", source.Sha256);
            command.Parameters.AddWithValue("message_count", data.Canonical.Counts.TotalMessageCount);
            command.Parameters.AddWithValue("record_count", data.Canonical.Counts.RecordCount);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var warning in data.Canonical.Warnings)
        {
            await using var command = session.Connection.CreateCommand();
            command.Transaction = session.Transaction;
            command.CommandText = """
                insert into app.fit_processing_warnings (
                  owner_id, fit_processing_attempt_id, code, message, occurrence_count)
                values (@owner_id, @attempt_id, @code, @message, @count);
                """;
            command.Parameters.AddWithValue("owner_id", claim.OwnerId);
            command.Parameters.AddWithValue("attempt_id", attemptId);
            command.Parameters.AddWithValue("code", warning.Code);
            command.Parameters.AddWithValue("message", warning.Message);
            command.Parameters.AddWithValue("count", checked((int)warning.Count));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var schema in data.Canonical.MessageSchemas)
        {
            foreach (var field in schema.Fields)
            {
                await using var command = session.Connection.CreateCommand();
                command.Transaction = session.Transaction;
                command.CommandText = """
                    insert into app.fit_schema_observations (
                      owner_id, fit_processing_attempt_id, message_name,
                      global_message_number, field_name, field_number, base_type,
                      unit, profile_version, is_developer_field, valid_count, invalid_count)
                    values (
                      @owner_id, @attempt_id, @message_name,
                      @global_message_number, @field_name, @field_number, @base_type,
                      @unit, @profile_version, @is_developer, @valid_count, @invalid_count);
                    """;
                command.Parameters.AddWithValue("owner_id", claim.OwnerId);
                command.Parameters.AddWithValue("attempt_id", attemptId);
                AddNullable(command, "message_name", NpgsqlDbType.Text, schema.OriginalName);
                command.Parameters.AddWithValue("global_message_number", (int)schema.GlobalMessageNumber);
                AddNullable(command, "field_name", NpgsqlDbType.Text, field.OriginalName);
                command.Parameters.AddWithValue("field_number", (int)field.FieldNumber);
                command.Parameters.AddWithValue("base_type", field.BaseTypeName);
                AddNullable(command, "unit", NpgsqlDbType.Text, field.Units);
                AddNullable(command, "profile_version", NpgsqlDbType.Text, field.ProfileType);
                command.Parameters.AddWithValue("is_developer", field.IsDeveloperField);
                command.Parameters.AddWithValue("valid_count", checked((int)field.ValidValueCount));
                command.Parameters.AddWithValue("invalid_count", checked((int)field.InvalidValueCount));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private static async Task UpsertActivitySummaryAsync(
        OwnerDbSession session,
        Guid ownerId,
        FitSourceDescriptor source,
        FitActivitySummary summary,
        Guid activityId,
        bool created,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = created
            ? """
              insert into app.activities (
                id, owner_id, garmin_activity_id, activity_type, activity_category,
                modality, started_at_local, started_at_utc, title, distance_m,
                duration_seconds, elapsed_seconds, average_pace_seconds_per_km,
                average_speed_mps, calories, average_heart_rate_bpm,
                max_heart_rate_bpm, average_cadence_spm, average_power_w,
                elevation_gain_m, lap_count, validation_status)
              values (
                @activity_id, @owner_id, @garmin_activity_id, @activity_type,
                @activity_category, @modality, @started_at_local, @started_at_utc,
                @title, @distance_m, @duration_seconds, @elapsed_seconds,
                @average_pace, @average_speed, @calories, @average_hr, @max_hr,
                @average_cadence, @average_power, @elevation_gain, @lap_count,
                'published');
              """
            : """
              update app.activities
              set garmin_activity_id = coalesce(garmin_activity_id, @garmin_activity_id),
                  activity_type = @activity_type,
                  activity_category = @activity_category,
                  modality = coalesce(@modality, modality),
                  started_at_utc = coalesce(@started_at_utc, started_at_utc),
                  title = coalesce(title, @title),
                  distance_m = coalesce(@distance_m, distance_m),
                  duration_seconds = coalesce(@duration_seconds, duration_seconds),
                  elapsed_seconds = coalesce(@elapsed_seconds, elapsed_seconds),
                  average_pace_seconds_per_km = coalesce(@average_pace, average_pace_seconds_per_km),
                  average_speed_mps = coalesce(@average_speed, average_speed_mps),
                  calories = coalesce(@calories, calories),
                  average_heart_rate_bpm = coalesce(@average_hr, average_heart_rate_bpm),
                  max_heart_rate_bpm = coalesce(@max_hr, max_heart_rate_bpm),
                  average_cadence_spm = coalesce(@average_cadence, average_cadence_spm),
                  average_power_w = coalesce(@average_power, average_power_w),
                  elevation_gain_m = coalesce(@elevation_gain, elevation_gain_m),
                  lap_count = coalesce(@lap_count, lap_count),
                  validation_status = 'published'
              where id = @activity_id;
              """;
        command.Parameters.AddWithValue("activity_id", activityId);
        command.Parameters.AddWithValue("owner_id", ownerId);
        command.Parameters.AddWithValue("garmin_activity_id", source.GarminActivityId!.Value);
        command.Parameters.AddWithValue("activity_type", summary.ActivityType);
        command.Parameters.AddWithValue("activity_category", summary.ActivityCategory);
        AddNullable(command, "modality", NpgsqlDbType.Text, summary.Modality);
        command.Parameters.AddWithValue(
            "started_at_local",
            NpgsqlDbType.Timestamp,
            summary.StartedAtLocal!.Value);
        AddNullable(command, "started_at_utc", NpgsqlDbType.TimestampTz, summary.StartedAtUtc);
        AddNullable(command, "title", NpgsqlDbType.Text, summary.Title);
        AddNullable(command, "distance_m", NpgsqlDbType.Numeric, summary.DistanceM);
        AddNullable(command, "duration_seconds", NpgsqlDbType.Numeric, summary.DurationSeconds);
        AddNullable(command, "elapsed_seconds", NpgsqlDbType.Numeric, summary.ElapsedSeconds);
        AddNullable(command, "average_pace", NpgsqlDbType.Numeric, summary.AveragePaceSecondsPerKm);
        AddNullable(command, "average_speed", NpgsqlDbType.Numeric, summary.AverageSpeedMps);
        AddNullable(command, "calories", NpgsqlDbType.Numeric, summary.Calories);
        AddNullable(command, "average_hr", NpgsqlDbType.Numeric, summary.AverageHeartRateBpm);
        AddNullable(command, "max_hr", NpgsqlDbType.Numeric, summary.MaxHeartRateBpm);
        AddNullable(command, "average_cadence", NpgsqlDbType.Numeric, summary.AverageCadenceSpm);
        AddNullable(command, "average_power", NpgsqlDbType.Numeric, summary.AveragePowerW);
        AddNullable(command, "elevation_gain", NpgsqlDbType.Numeric, summary.ElevationGainM);
        AddNullable(command, "lap_count", NpgsqlDbType.Integer, summary.LapCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Guid> InsertObservationAsync(
        OwnerDbSession session,
        ClaimedFitRun claim,
        FitSourceDescriptor source,
        FitActivityData data,
        Guid activityId,
        CancellationToken cancellationToken)
    {
        var itemId = await GetItemIdAsync(session, claim.RunId, cancellationToken);
        var observationId = Guid.NewGuid();
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            insert into app.activity_source_observations (
              id, owner_id, activity_id, source_file_id, ingestion_item_id,
              source_class, observed_keys, summary_payload, linking_result, observed_at)
            values (
              @id, @owner_id, @activity_id, @source_file_id, @item_id,
              'fit_session', @observed_keys, @summary_payload,
              'garmin_id_or_strict_historical_match', now());
            """;
        command.Parameters.AddWithValue("id", observationId);
        command.Parameters.AddWithValue("owner_id", claim.OwnerId);
        command.Parameters.AddWithValue("activity_id", activityId);
        command.Parameters.AddWithValue("source_file_id", source.SourceFileId);
        command.Parameters.AddWithValue("item_id", itemId);
        command.Parameters.AddWithValue(
            "observed_keys",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(new
            {
                GarminActivityId = source.GarminActivityId,
                FitSha256 = source.Sha256
            }));
        command.Parameters.AddWithValue(
            "summary_payload",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(data.Summary));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return observationId;
    }

    private static async Task SelectFieldSourcesAsync(
        OwnerDbSession session,
        FitActivitySummary summary,
        Guid activityId,
        Guid observationId,
        bool activityCreated,
        CancellationToken cancellationToken)
    {
        var present = new HashSet<string>(StringComparer.Ordinal)
        {
            "activity_type", "activity_category", "started_at_local"
        };
        if (summary.Modality is not null) present.Add("modality");
        if (summary.StartedAtUtc is not null) present.Add("started_at_utc");
        if (activityCreated && summary.Title is not null) present.Add("title");
        if (summary.DistanceM is not null) present.Add("distance_m");
        if (summary.DurationSeconds is not null) present.Add("duration_seconds");
        if (summary.ElapsedSeconds is not null) present.Add("elapsed_seconds");
        if (summary.AveragePaceSecondsPerKm is not null) present.Add("average_pace_seconds_per_km");
        if (summary.AverageSpeedMps is not null) present.Add("average_speed_mps");
        if (summary.Calories is not null) present.Add("calories");
        if (summary.AverageHeartRateBpm is not null) present.Add("average_heart_rate_bpm");
        if (summary.MaxHeartRateBpm is not null) present.Add("max_heart_rate_bpm");
        if (summary.AverageCadenceSpm is not null) present.Add("average_cadence_spm");
        if (summary.AveragePowerW is not null) present.Add("average_power_w");
        if (summary.ElevationGainM is not null) present.Add("elevation_gain_m");
        if (summary.LapCount is not null) present.Add("lap_count");

        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            insert into app.activity_field_sources (
              owner_id, activity_id, field_name, source_observation_id, precedence_rule)
            select app.current_owner_id(), @activity_id, field_name, @observation_id,
                   'fit_validated_recorded_value'
            from unnest(@field_names::text[]) as field_name
            on conflict (activity_id, field_name) do update
            set source_observation_id = excluded.source_observation_id,
                precedence_rule = excluded.precedence_rule,
                selected_at = now();
            """;
        command.Parameters.AddWithValue("activity_id", activityId);
        command.Parameters.AddWithValue("observation_id", observationId);
        command.Parameters.AddWithValue(
            "field_names",
            SummaryFields.Where(present.Contains).ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertNormalizedDetailAsync(
        OwnerDbSession session,
        Guid ownerId,
        Guid activityId,
        Guid attemptId,
        FitActivityData data,
        CancellationToken cancellationToken)
    {
        var sessionIds = new Dictionary<int, Guid>();
        foreach (var item in data.Sessions)
        {
            var id = Guid.NewGuid();
            sessionIds[item.Sequence] = id;
            await using var command = session.Connection.CreateCommand();
            command.Transaction = session.Transaction;
            command.CommandText = """
                insert into app.activity_fit_sessions (
                  id, owner_id, activity_id, fit_processing_attempt_id, sequence,
                  sport, sub_sport, started_at_utc, duration_seconds, distance_m, summary)
                values (
                  @id, @owner_id, @activity_id, @attempt_id, @sequence,
                  @sport, @sub_sport, @started_at, @duration, @distance, @summary);
                """;
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("owner_id", ownerId);
            command.Parameters.AddWithValue("activity_id", activityId);
            command.Parameters.AddWithValue("attempt_id", attemptId);
            command.Parameters.AddWithValue("sequence", item.Sequence);
            command.Parameters.AddWithValue("sport", item.Sport);
            AddNullable(command, "sub_sport", NpgsqlDbType.Text, item.SubSport);
            AddNullable(command, "started_at", NpgsqlDbType.TimestampTz, item.StartedAtUtc);
            AddNullable(command, "duration", NpgsqlDbType.Numeric, item.DurationSeconds);
            AddNullable(command, "distance", NpgsqlDbType.Numeric, item.DistanceM);
            command.Parameters.AddWithValue("summary", NpgsqlDbType.Jsonb, item.SummaryJson);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var item in data.Laps)
        {
            await using var command = session.Connection.CreateCommand();
            command.Transaction = session.Transaction;
            command.CommandText = """
                insert into app.activity_laps (
                  owner_id, activity_id, activity_fit_session_id,
                  fit_processing_attempt_id, lap_index, started_at_utc,
                  ended_at_utc, duration_seconds, distance_m, summary)
                values (
                  @owner_id, @activity_id, @fit_session_id, @attempt_id,
                  @lap_index, @started_at, @ended_at, @duration, @distance, @summary);
                """;
            command.Parameters.AddWithValue("owner_id", ownerId);
            command.Parameters.AddWithValue("activity_id", activityId);
            AddNullable(
                command,
                "fit_session_id",
                NpgsqlDbType.Uuid,
                item.SessionIndex is not null && sessionIds.TryGetValue(item.SessionIndex.Value, out var id)
                    ? id
                    : null);
            command.Parameters.AddWithValue("attempt_id", attemptId);
            command.Parameters.AddWithValue("lap_index", item.Index);
            AddNullable(command, "started_at", NpgsqlDbType.TimestampTz, item.StartedAtUtc);
            AddNullable(command, "ended_at", NpgsqlDbType.TimestampTz, item.EndedAtUtc);
            AddNullable(command, "duration", NpgsqlDbType.Numeric, item.DurationSeconds);
            AddNullable(command, "distance", NpgsqlDbType.Numeric, item.DistanceM);
            command.Parameters.AddWithValue("summary", NpgsqlDbType.Jsonb, item.SummaryJson);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var item in data.Events)
        {
            await using var command = session.Connection.CreateCommand();
            command.Transaction = session.Transaction;
            command.CommandText = """
                insert into app.activity_events (
                  owner_id, activity_id, fit_processing_attempt_id, event_index,
                  recorded_at_utc, event_name, event_type, event_group, event_data,
                  additional_fields)
                values (
                  @owner_id, @activity_id, @attempt_id, @event_index,
                  @recorded_at, @event_name, @event_type, @event_group, @event_data,
                  @additional_fields);
                """;
            command.Parameters.AddWithValue("owner_id", ownerId);
            command.Parameters.AddWithValue("activity_id", activityId);
            command.Parameters.AddWithValue("attempt_id", attemptId);
            command.Parameters.AddWithValue("event_index", item.Index);
            AddNullable(command, "recorded_at", NpgsqlDbType.TimestampTz, item.RecordedAtUtc);
            AddNullable(command, "event_name", NpgsqlDbType.Text, item.EventName);
            AddNullable(command, "event_type", NpgsqlDbType.Text, item.EventType);
            AddNullable(command, "event_group", NpgsqlDbType.Text, item.EventGroup);
            AddNullable(command, "event_data", NpgsqlDbType.Text, item.EventData);
            command.Parameters.AddWithValue(
                "additional_fields",
                NpgsqlDbType.Jsonb,
                item.AdditionalFieldsJson);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var item in data.Zones)
        {
            await using var command = session.Connection.CreateCommand();
            command.Transaction = session.Transaction;
            command.CommandText = """
                insert into app.activity_time_in_zones (
                  owner_id, activity_id, fit_processing_attempt_id, zone_type,
                  zone_index, lower_bound, upper_bound, duration_seconds, source_reference)
                values (
                  @owner_id, @activity_id, @attempt_id, @zone_type,
                  @zone_index, @lower_bound, @upper_bound, @duration, @source_reference);
                """;
            command.Parameters.AddWithValue("owner_id", ownerId);
            command.Parameters.AddWithValue("activity_id", activityId);
            command.Parameters.AddWithValue("attempt_id", attemptId);
            command.Parameters.AddWithValue("zone_type", item.ZoneType);
            command.Parameters.AddWithValue("zone_index", item.ZoneIndex);
            AddNullable(command, "lower_bound", NpgsqlDbType.Numeric, item.LowerBound);
            AddNullable(command, "upper_bound", NpgsqlDbType.Numeric, item.UpperBound);
            command.Parameters.AddWithValue("duration", item.DurationSeconds);
            command.Parameters.AddWithValue("source_reference", item.SourceReference);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var batch in data.Samples.Chunk(options.SampleBatchSize))
        {
            var payload = JsonSerializer.Serialize(batch.Select(item => new
            {
                sample_index = item.Index,
                recorded_at_utc = item.RecordedAtUtc,
                distance_m = item.DistanceM,
                latitude_degrees = item.LatitudeDegrees,
                longitude_degrees = item.LongitudeDegrees,
                altitude_m = item.AltitudeM,
                speed_mps = item.SpeedMps,
                heart_rate_bpm = item.HeartRateBpm,
                cadence_spm = item.CadenceSpm,
                power_w = item.PowerW,
                temperature_c = item.TemperatureC,
                additional_fields = item.AdditionalFieldsJson
            }));
            await using var command = session.Connection.CreateCommand();
            command.Transaction = session.Transaction;
            command.CommandText = """
                insert into app.activity_samples (
                  owner_id, activity_id, fit_processing_attempt_id, sample_index,
                  recorded_at_utc, distance_m, latitude_degrees, longitude_degrees,
                  altitude_m, speed_mps, heart_rate_bpm, cadence_spm, power_w,
                  temperature_c, additional_fields)
                select @owner_id, @activity_id, @attempt_id, sample_index,
                       recorded_at_utc, distance_m, latitude_degrees, longitude_degrees,
                       altitude_m, speed_mps, heart_rate_bpm, cadence_spm, power_w,
                       temperature_c, additional_fields::jsonb
                from jsonb_to_recordset(@payload) as sample(
                  sample_index integer,
                  recorded_at_utc timestamptz,
                  distance_m numeric,
                  latitude_degrees numeric,
                  longitude_degrees numeric,
                  altitude_m numeric,
                  speed_mps numeric,
                  heart_rate_bpm numeric,
                  cadence_spm numeric,
                  power_w numeric,
                  temperature_c numeric,
                  additional_fields text);
                """;
            command.Parameters.AddWithValue("owner_id", ownerId);
            command.Parameters.AddWithValue("activity_id", activityId);
            command.Parameters.AddWithValue("attempt_id", attemptId);
            command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, payload);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task CompleteAppliedAsync(
        OwnerDbSession session,
        ClaimedFitRun claim,
        FitSourceDescriptor source,
        Guid activityId,
        string action,
        CancellationToken cancellationToken)
    {
        await CompleteAsync(
            session,
            claim,
            source,
            activityId,
            "applied",
            action,
            cancellationToken);
    }

    private static async Task CompleteSkippedAsync(
        OwnerDbSession session,
        ClaimedFitRun claim,
        FitSourceDescriptor source,
        Guid activityId,
        CancellationToken cancellationToken)
    {
        await CompleteAsync(
            session,
            claim,
            source,
            activityId,
            "skipped",
            "duplicate_id_and_hash",
            cancellationToken);
    }

    private static async Task CompleteAsync(
        OwnerDbSession session,
        ClaimedFitRun claim,
        FitSourceDescriptor source,
        Guid activityId,
        string itemStatus,
        string action,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            update app.ingestion_runs
            set status = 'succeeded', finished_at = now(), item_count = 1,
                success_count = 1, failure_count = 0, lease_owner = null,
                lease_until = null, heartbeat_at = now()
            where id = @run_id;

            update app.source_files
            set status = 'accepted'
            where id = @source_file_id;

            update app.ingestion_items
            set target_activity_id = @activity_id, status = @item_status,
                action = @action, error_code = null, error_message = null,
                retryable = false
            where ingestion_run_id = @run_id;

            insert into app.audit_events (
              owner_id, actor_type, action, entity_type, entity_id,
              correlation_id, changed_fields)
            values (
              @owner_id, 'worker', 'fit_ingestion.succeeded', 'ingestion_run',
              @run_id, @correlation_id,
              array['status', 'activity_id', 'fit_processing_attempt']);
            """;
        command.Parameters.AddWithValue("run_id", claim.RunId);
        command.Parameters.AddWithValue("source_file_id", source.SourceFileId);
        command.Parameters.AddWithValue("activity_id", activityId);
        command.Parameters.AddWithValue("item_status", itemStatus);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("owner_id", claim.OwnerId);
        command.Parameters.AddWithValue("correlation_id", claim.CorrelationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task HeartbeatAsync(
        ClaimedFitRun claim,
        CancellationToken cancellationToken)
    {
        await using var session = await ownerDataSource.OpenWorkerAsync(
            claim.OwnerId,
            cancellationToken);
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
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The FIT ingestion lease was lost.");
        }
        await session.CommitAsync(cancellationToken);
    }

    private async Task QuarantineAsync(
        ClaimedFitRun claim,
        string code,
        string message,
        FitActivityData? data,
        CancellationToken cancellationToken)
    {
        await using var session = await ownerDataSource.OpenWorkerAsync(
            claim.OwnerId,
            cancellationToken);
        var itemId = await GetItemIdAsync(session, claim.RunId, cancellationToken);
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            update app.ingestion_runs
            set status = 'quarantined', finished_at = now(), item_count = 1,
                failure_count = 1, lease_owner = null, lease_until = null,
                heartbeat_at = now()
            where id = @run_id;

            update app.source_files set status = 'quarantined'
            where id = @source_file_id;

            update app.ingestion_items
            set status = 'quarantined', action = 'quarantined',
                error_code = @error_code, error_message = @error_message,
                retryable = false
            where id = @item_id;

            insert into app.quarantine_cases (
              owner_id, source_file_id, ingestion_item_id, reason_code, details)
            values (
              @owner_id, @source_file_id, @item_id, @error_code,
              jsonb_build_object('message', @error_message));
            """;
        command.Parameters.AddWithValue("run_id", claim.RunId);
        command.Parameters.AddWithValue("source_file_id", claim.SourceFileId);
        command.Parameters.AddWithValue("item_id", itemId);
        command.Parameters.AddWithValue("owner_id", claim.OwnerId);
        command.Parameters.AddWithValue("error_code", code);
        command.Parameters.AddWithValue("error_message", message);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var attempt = session.Connection.CreateCommand();
        attempt.Transaction = session.Transaction;
        attempt.CommandText = """
            insert into app.fit_processing_attempts (
              owner_id, source_file_id, ingestion_run_id, processor_version,
              sdk_version, schema_version, signature_valid, declared_size_valid,
              crc_valid, full_read_valid, sha256, message_count, record_count,
              status, is_current)
            select @owner_id, @source_file_id, @run_id, @processor_version,
                   @sdk_version, @schema_version, @signature_valid,
                   @declared_size_valid, @crc_valid, @full_read_valid,
                   object.sha256, @message_count, @record_count,
                   'quarantined', false
            from app.source_files as source
            join app.stored_objects as object
              on object.owner_id = source.owner_id
             and object.id = source.stored_object_id
            where source.id = @source_file_id;
            """;
        attempt.Parameters.AddWithValue("owner_id", claim.OwnerId);
        attempt.Parameters.AddWithValue("source_file_id", claim.SourceFileId);
        attempt.Parameters.AddWithValue("run_id", claim.RunId);
        attempt.Parameters.AddWithValue(
            "processor_version",
            data?.Canonical.Canonicalizer.Version ?? CanonicalFitProcessor.ProcessorVersion);
        attempt.Parameters.AddWithValue(
            "sdk_version",
            data?.Canonical.Canonicalizer.DecoderVersion ?? CanonicalFitProcessor.SdkVersion);
        attempt.Parameters.AddWithValue(
            "schema_version",
            data?.Canonical.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "1");
        attempt.Parameters.AddWithValue("signature_valid", data?.Canonical.Validation.IsFit ?? false);
        attempt.Parameters.AddWithValue("declared_size_valid", code != "fit_source_size_mismatch");
        attempt.Parameters.AddWithValue("crc_valid", data?.Canonical.Validation.IntegrityValid ?? false);
        attempt.Parameters.AddWithValue("full_read_valid", data?.Canonical.Validation.ReadSuccessful ?? false);
        attempt.Parameters.AddWithValue("message_count", data?.Canonical.Counts.TotalMessageCount ?? 0);
        attempt.Parameters.AddWithValue("record_count", data?.Canonical.Counts.RecordCount ?? 0);
        await attempt.ExecuteNonQueryAsync(cancellationToken);
        await session.CommitAsync(cancellationToken);
    }

    private async Task RetryOrFailAsync(
        ClaimedFitRun claim,
        string code,
        CancellationToken cancellationToken)
    {
        var terminal = claim.AttemptCount >= options.MaxAttempts;
        var delaySeconds = Math.Min(300, 5 * (1 << Math.Min(6, claim.AttemptCount - 1)));
        await using var session = await ownerDataSource.OpenWorkerAsync(
            claim.OwnerId,
            cancellationToken);
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            update app.ingestion_runs
            set status = @status,
                finished_at = case when @terminal then now() else null end,
                next_attempt_at = case when @terminal then null else now() + make_interval(secs => @delay_seconds) end,
                failure_count = case when @terminal then 1 else failure_count end,
                lease_owner = null, lease_until = null, heartbeat_at = now()
            where id = @run_id;

            update app.ingestion_items
            set status = case when @terminal then 'failed' else 'pending' end,
                action = case when @terminal then 'failed' else 'retry_scheduled' end,
                error_code = @error_code,
                error_message = 'The FIT ingestion attempt failed; inspect the sanitized error code.',
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

    private static async Task<Guid> GetItemIdAsync(
        OwnerDbSession session,
        Guid runId,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = "select id from app.ingestion_items where ingestion_run_id = @run_id and ordinal = 1;";
        command.Parameters.AddWithValue("run_id", runId);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("The FIT ingestion envelope is missing."));
    }

    private static void AddNullable(
        NpgsqlCommand command,
        string name,
        NpgsqlDbType type,
        object? value)
    {
        command.Parameters.Add(name, type).Value = value ?? DBNull.Value;
    }

    private sealed record ClaimedFitRun(
        Guid RunId,
        Guid OwnerId,
        Guid SourceFileId,
        Guid CorrelationId,
        int AttemptCount,
        string RunType,
        string LeaseOwner);

    private sealed record FitSourceDescriptor(
        Guid SourceFileId,
        string OriginalName,
        long? GarminActivityId,
        string ObjectPath,
        string Sha256,
        long SizeBytes);

    private sealed record ResolvedIdentity(Guid ActivityId, bool Created, bool Skip);

    private sealed class FitQuarantineException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
