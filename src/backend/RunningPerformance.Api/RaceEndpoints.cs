using System.Security.Claims;
using Npgsql;
using NpgsqlTypes;
using RunningPerformance.Api.Authentication;
using RunningPerformance.Api.Http;
using RunningPerformance.Infrastructure.Database;

namespace RunningPerformance.Api.Features;

public static class RaceEndpoints
{
    public static IEndpointRouteBuilder MapRaceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/races").WithTags("Races");

        group.MapGet("/", GetRacesAsync)
            .WithName("GetRaces")
            .Produces<IReadOnlyList<TargetRaceResponse>>();
        group.MapPost("/", CreateRaceAsync)
            .WithName("CreateRace")
            .Produces<TargetRaceResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();
        group.MapPut("/{id:guid}", UpdateRaceAsync)
            .WithName("UpdateRace")
            .Produces<TargetRaceResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound);
        group.MapGet("/{id:guid}/goals", GetRaceGoalsAsync)
            .WithName("GetRaceGoals")
            .Produces<IReadOnlyList<RaceGoalResponse>>()
            .Produces(StatusCodes.Status404NotFound);
        group.MapPost("/{id:guid}/goals", CreateRaceGoalAsync)
            .WithName("CreateRaceGoal")
            .Produces<RaceGoalResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> GetRacesAsync(
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var races = await ReadRacesAsync(session, null, cancellationToken);
        await session.CommitAsync(cancellationToken);
        return Results.Ok(races);
    }

    private static async Task<IResult> CreateRaceAsync(
        SaveTargetRaceRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var errors = RequestValidation.Race(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var id = Guid.NewGuid();
        await SaveRaceAsync(session, ownerId, id, request, isUpdate: false, cancellationToken);
        await AuditWriter.WriteAsync(
            session,
            ownerId,
            "race.created",
            "target_race",
            id,
            httpContext.GetCorrelationId(),
            ["name", "race_date", "distance_m", "location", "priority", "status", "timezone_name"],
            cancellationToken);
        var created = (await ReadRacesAsync(session, id, cancellationToken)).Single();
        await session.CommitAsync(cancellationToken);
        return Results.Created($"/api/v1/races/{id}", created);
    }

    private static async Task<IResult> UpdateRaceAsync(
        Guid id,
        SaveTargetRaceRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var errors = RequestValidation.Race(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var updated = await SaveRaceAsync(session, ownerId, id, request, isUpdate: true, cancellationToken);
        if (!updated)
        {
            return Results.NotFound();
        }

        await AuditWriter.WriteAsync(
            session,
            ownerId,
            "race.updated",
            "target_race",
            id,
            httpContext.GetCorrelationId(),
            ["name", "race_date", "distance_m", "location", "priority", "status", "timezone_name"],
            cancellationToken);
        var race = (await ReadRacesAsync(session, id, cancellationToken)).Single();
        await session.CommitAsync(cancellationToken);
        return Results.Ok(race);
    }

    private static async Task<IResult> GetRaceGoalsAsync(
        Guid id,
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        if (!await RaceExistsAsync(session, id, cancellationToken))
        {
            return Results.NotFound();
        }

        var goals = new List<RaceGoalResponse>();
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select
                  id,
                  version_number,
                  goal_time_seconds,
                  goal_pace_seconds_per_km,
                  confidence,
                  rationale,
                  supersedes_id,
                  effective_at
                from app.race_goal_versions
                where target_race_id = @race_id
                order by version_number desc;
                """;
            command.Parameters.AddWithValue("race_id", id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                goals.Add(ReadGoal(reader));
            }
        }

        await session.CommitAsync(cancellationToken);
        return Results.Ok(goals);
    }

    private static async Task<IResult> CreateRaceGoalAsync(
        Guid id,
        CreateRaceGoalRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var errors = RequestValidation.Goal(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            select
              id,
              version_number,
              goal_time_seconds,
              goal_pace_seconds_per_km,
              confidence,
              rationale,
              supersedes_id,
              effective_at
            from app.create_race_goal_version(
              @race_id,
              @goal_time_seconds,
              @goal_pace_seconds_per_km,
              @confidence,
              @rationale,
              @correlation_id);
            """;
        command.Parameters.AddWithValue("race_id", id);
        AddNullable(command, "goal_time_seconds", NpgsqlDbType.Numeric, request.GoalTimeSeconds);
        AddNullable(command, "goal_pace_seconds_per_km", NpgsqlDbType.Numeric, request.GoalPaceSecondsPerKm);
        AddNullable(command, "confidence", NpgsqlDbType.Text, request.Confidence);
        command.Parameters.AddWithValue("rationale", request.Rationale.Trim());
        command.Parameters.AddWithValue("correlation_id", httpContext.GetCorrelationId());

        RaceGoalResponse? created = null;
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                created = ReadGoal(reader);
            }
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.NoDataFound)
        {
            return Results.NotFound();
        }

        if (created is null)
        {
            return Results.NotFound();
        }

        await session.CommitAsync(cancellationToken);
        return Results.Created($"/api/v1/races/{id}/goals/{created.Id}", created);
    }

    private static async Task<IReadOnlyList<TargetRaceResponse>> ReadRacesAsync(
        OwnerDbSession session,
        Guid? raceId,
        CancellationToken cancellationToken)
    {
        var races = new List<TargetRaceResponse>();
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            select
              r.id,
              r.name,
              r.race_date,
              r.distance_m,
              r.location,
              r.priority,
              r.status,
              r.timezone_name,
              r.updated_at,
              g.id,
              g.version_number,
              g.goal_time_seconds,
              g.goal_pace_seconds_per_km,
              g.confidence,
              g.rationale,
              g.supersedes_id,
              g.effective_at
            from app.target_races r
            left join app.race_goal_versions g
              on g.owner_id = r.owner_id
             and g.target_race_id = r.id
             and g.is_current
            where @race_id is null or r.id = @race_id
            order by r.race_date, r.id;
            """;
        AddNullable(command, "race_id", NpgsqlDbType.Uuid, raceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var goal = reader.IsDBNull(9)
                ? null
                : new RaceGoalResponse(
                    reader.GetGuid(9),
                    reader.GetInt32(10),
                    GetNullable<decimal>(reader, 11),
                    GetNullable<decimal>(reader, 12),
                    GetNullableString(reader, 13),
                    reader.GetString(14),
                    GetNullable<Guid>(reader, 15),
                    reader.GetDateTime(16));

            races.Add(new TargetRaceResponse(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetFieldValue<DateOnly>(2),
                reader.GetDecimal(3),
                GetNullableString(reader, 4),
                reader.GetString(5),
                reader.GetString(6),
                GetNullableString(reader, 7),
                reader.GetDateTime(8),
                goal));
        }

        return races;
    }

    private static async Task<bool> SaveRaceAsync(
        OwnerDbSession session,
        Guid ownerId,
        Guid id,
        SaveTargetRaceRequest request,
        bool isUpdate,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = isUpdate
            ? """
                update app.target_races
                set name = @name,
                    race_date = @race_date,
                    distance_m = @distance_m,
                    location = @location,
                    priority = @priority,
                    status = @status,
                    timezone_name = @timezone_name
                where id = @id;
                """
            : """
                insert into app.target_races (
                  id, owner_id, name, race_date, distance_m, location, priority, status, timezone_name)
                values (
                  @id, @owner_id, @name, @race_date, @distance_m, @location, @priority, @status, @timezone_name);
                """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("owner_id", ownerId);
        command.Parameters.AddWithValue("name", request.Name.Trim());
        command.Parameters.AddWithValue("race_date", request.RaceDate);
        command.Parameters.AddWithValue("distance_m", request.DistanceM);
        AddNullable(command, "location", NpgsqlDbType.Text, CleanOptional(request.Location));
        command.Parameters.AddWithValue("priority", request.Priority);
        command.Parameters.AddWithValue("status", request.Status);
        AddNullable(command, "timezone_name", NpgsqlDbType.Text, CleanOptional(request.TimezoneName));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task<bool> RaceExistsAsync(
        OwnerDbSession session,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = "select exists(select 1 from app.target_races where id = @id);";
        command.Parameters.AddWithValue("id", id);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static RaceGoalResponse ReadGoal(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetInt32(1),
            GetNullable<decimal>(reader, 2),
            GetNullable<decimal>(reader, 3),
            GetNullableString(reader, 4),
            reader.GetString(5),
            GetNullable<Guid>(reader, 6),
            reader.GetDateTime(7));

    private static T? GetNullable<T>(NpgsqlDataReader reader, int ordinal) where T : struct =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<T>(ordinal);

    private static string? GetNullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static void AddNullable<T>(
        NpgsqlCommand command,
        string name,
        NpgsqlDbType type,
        T? value)
    {
        command.Parameters.Add(name, type).Value = value is null ? DBNull.Value : value;
    }

    private static string? CleanOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
