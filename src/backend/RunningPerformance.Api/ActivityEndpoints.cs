using System.Security.Claims;
using System.Text.Json;
using Npgsql;
using RunningPerformance.Api.Authentication;
using RunningPerformance.Infrastructure.Database;

namespace RunningPerformance.Api.Features;

public static class ActivityEndpoints
{
    public static IEndpointRouteBuilder MapActivityEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/activities").WithTags("Activities");

        group.MapGet("/", GetActivitiesAsync)
            .WithName("GetActivities")
            .Produces<ActivityPageResponse>()
            .ProducesValidationProblem();
        group.MapGet("/{id:guid}", GetActivityAsync)
            .WithName("GetActivity")
            .Produces<ActivityDetailResponse>()
            .Produces(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> GetActivitiesAsync(
        string? activityType,
        string? category,
        string? modality,
        DateOnly? from,
        DateOnly? to,
        decimal? minDistanceM,
        decimal? maxDistanceM,
        string? sort,
        string? direction,
        int page,
        int pageSize,
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        page = page == 0 ? 1 : page;
        pageSize = pageSize == 0 ? 50 : pageSize;
        var errors = new Dictionary<string, string[]>();
        if (page < 1)
        {
            errors["page"] = ["Page must be greater than zero."];
        }

        if (pageSize is < 1 or > 100)
        {
            errors["pageSize"] = ["Page size must be between 1 and 100."];
        }

        if (from.HasValue && to.HasValue && from > to)
        {
            errors["from"] = ["From must not be later than to."];
        }

        if (minDistanceM is < 0 || maxDistanceM is < 0)
        {
            errors["distance"] = ["Distance filters must not be negative."];
        }

        if (minDistanceM.HasValue && maxDistanceM.HasValue && minDistanceM > maxDistanceM)
        {
            errors["minDistanceM"] = ["Minimum distance must not exceed maximum distance."];
        }

        var sortColumn = sort?.ToLowerInvariant() switch
        {
            null or "startedat" => "started_at_local",
            "distance" => "distance_m",
            "duration" => "duration_seconds",
            _ => string.Empty
        };
        if (sortColumn.Length == 0)
        {
            errors["sort"] = ["Sort must be startedAt, distance, or duration."];
        }

        var sortDirection = direction?.ToLowerInvariant() switch
        {
            null or "desc" => "desc",
            "asc" => "asc",
            _ => string.Empty
        };
        if (sortDirection.Length == 0)
        {
            errors["direction"] = ["Direction must be asc or desc."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var filters = new List<string>();
        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        AddFilter(command, filters, "activity_type", "activity_type", activityType);
        AddFilter(command, filters, "activity_category", "category", category);
        AddFilter(command, filters, "modality", "modality", modality);
        if (from.HasValue)
        {
            filters.Add("started_at_local >= @from");
            command.Parameters.AddWithValue("from", from.Value.ToDateTime(TimeOnly.MinValue));
        }

        if (to.HasValue)
        {
            filters.Add("started_at_local < @to_exclusive");
            command.Parameters.AddWithValue("to_exclusive", to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));
        }

        if (minDistanceM.HasValue)
        {
            filters.Add("distance_m >= @min_distance_m");
            command.Parameters.AddWithValue("min_distance_m", minDistanceM.Value);
        }

        if (maxDistanceM.HasValue)
        {
            filters.Add("distance_m <= @max_distance_m");
            command.Parameters.AddWithValue("max_distance_m", maxDistanceM.Value);
        }

        var where = filters.Count == 0 ? string.Empty : $"where {string.Join(" and ", filters)}";
        command.CommandText = $"""
            select count(*)
            from app.activities
            {where};

            select
              id, provisional_activity_key, garmin_activity_id,
              activity_type, activity_category, modality, started_at_local,
              title, distance_m, duration_seconds, average_pace_seconds_per_km,
              average_heart_rate_bpm, max_heart_rate_bpm, validation_status
            from app.activities
            {where}
            order by {sortColumn} {sortDirection} nulls last, id {sortDirection}
            limit @limit offset @offset;
            """;
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", checked((page - 1) * pageSize));

        var items = new List<ActivitySummaryResponse>();
        long total;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            total = reader.GetInt64(0);
            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(ReadSummary(reader));
            }
        }

        await session.CommitAsync(cancellationToken);
        return Results.Ok(new ActivityPageResponse(items, total, page, pageSize));
    }

    private static async Task<IResult> GetActivityAsync(
        Guid id,
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        ActivityDetailResponse? detail = null;
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select
                  id, provisional_activity_key, garmin_activity_id,
                  activity_type, activity_category, modality, started_at_local,
                  title, distance_m, duration_seconds, average_pace_seconds_per_km,
                  average_heart_rate_bpm, max_heart_rate_bpm, validation_status,
                  started_at_utc, timezone_name, utc_offset_minutes,
                  moving_seconds, elapsed_seconds, average_speed_mps, calories,
                  average_cadence_spm, average_power_w, elevation_gain_m, lap_count
                from app.activities
                where id = @id;
                """;
            command.Parameters.AddWithValue("id", id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                detail = new(
                    ReadSummary(reader),
                    GetNullableValue<DateTime>(reader, 14),
                    GetNullableString(reader, 15),
                    GetNullableValue<short>(reader, 16),
                    GetNullableValue<decimal>(reader, 17),
                    GetNullableValue<decimal>(reader, 18),
                    GetNullableValue<decimal>(reader, 19),
                    GetNullableValue<decimal>(reader, 20),
                    GetNullableValue<decimal>(reader, 21),
                    GetNullableValue<decimal>(reader, 22),
                    GetNullableValue<decimal>(reader, 23),
                    GetNullableValue<int>(reader, 24),
                    []);
            }
        }

        if (detail is null)
        {
            return Results.NotFound();
        }

        var sources = new List<ActivitySourceResponse>();
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select
                  observation.id, observation.source_class, observation.source_row_number,
                  observation.linking_result, observation.observed_at,
                  observation.summary_payload::text,
                  source.id, source.original_name, object.sha256,
                  item.ingestion_run_id
                from app.activity_source_observations as observation
                left join app.source_files as source
                  on source.owner_id = observation.owner_id
                 and source.id = observation.source_file_id
                left join app.stored_objects as object
                  on object.owner_id = source.owner_id
                 and object.id = source.stored_object_id
                left join app.ingestion_items as item
                  on item.owner_id = observation.owner_id
                 and item.id = observation.ingestion_item_id
                where observation.activity_id = @activity_id
                order by observation.created_at desc;
                """;
            command.Parameters.AddWithValue("activity_id", id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                using var payload = JsonDocument.Parse(reader.GetString(5));
                sources.Add(new(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    GetNullableValue<int>(reader, 2),
                    GetNullableString(reader, 3),
                    GetNullableValue<DateTime>(reader, 4),
                    payload.RootElement.Clone(),
                    GetNullableValue<Guid>(reader, 6),
                    GetNullableString(reader, 7),
                    GetNullableString(reader, 8),
                    GetNullableValue<Guid>(reader, 9)));
            }
        }

        await session.CommitAsync(cancellationToken);
        return Results.Ok(detail with { Sources = sources });
    }

    private static ActivitySummaryResponse ReadSummary(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        GetNullableString(reader, 1),
        GetNullableValue<long>(reader, 2),
        reader.GetString(3),
        GetNullableString(reader, 4),
        GetNullableString(reader, 5),
        reader.GetDateTime(6),
        GetNullableString(reader, 7),
        GetNullableValue<decimal>(reader, 8),
        GetNullableValue<decimal>(reader, 9),
        GetNullableValue<decimal>(reader, 10),
        GetNullableValue<decimal>(reader, 11),
        GetNullableValue<decimal>(reader, 12),
        reader.GetString(13));

    private static void AddFilter(
        NpgsqlCommand command,
        ICollection<string> filters,
        string column,
        string parameter,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            filters.Add($"{column} = @{parameter}");
            command.Parameters.AddWithValue(parameter, value.Trim());
        }
    }

    private static T? GetNullableValue<T>(NpgsqlDataReader reader, int ordinal)
        where T : struct =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<T>(ordinal);

    private static string? GetNullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}

public sealed record ActivitySummaryResponse(
    Guid Id,
    string? ProvisionalActivityKey,
    long? GarminActivityId,
    string ActivityType,
    string? ActivityCategory,
    string? Modality,
    DateTime StartedAtLocal,
    string? Title,
    decimal? DistanceM,
    decimal? DurationSeconds,
    decimal? AveragePaceSecondsPerKm,
    decimal? AverageHeartRateBpm,
    decimal? MaxHeartRateBpm,
    string ValidationStatus);

public sealed record ActivityPageResponse(
    IReadOnlyList<ActivitySummaryResponse> Items,
    long Total,
    int Page,
    int PageSize);

public sealed record ActivitySourceResponse(
    Guid Id,
    string SourceClass,
    int? SourceRowNumber,
    string? LinkingResult,
    DateTime? ObservedAt,
    JsonElement Summary,
    Guid? SourceFileId,
    string? OriginalName,
    string? Sha256,
    Guid? IngestionRunId);

public sealed record ActivityDetailResponse(
    ActivitySummaryResponse Activity,
    DateTime? StartedAtUtc,
    string? TimezoneName,
    short? UtcOffsetMinutes,
    decimal? MovingSeconds,
    decimal? ElapsedSeconds,
    decimal? AverageSpeedMps,
    decimal? Calories,
    decimal? AverageCadenceSpm,
    decimal? AveragePowerW,
    decimal? ElevationGainM,
    int? LapCount,
    IReadOnlyList<ActivitySourceResponse> Sources);
