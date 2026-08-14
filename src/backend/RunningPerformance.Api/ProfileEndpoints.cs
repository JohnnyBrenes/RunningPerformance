using System.Security.Claims;
using Npgsql;
using NpgsqlTypes;
using RunningPerformance.Api.Authentication;
using RunningPerformance.Api.Http;
using RunningPerformance.Infrastructure.Database;

namespace RunningPerformance.Api.Features;

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1").WithTags("Profile");

        group.MapGet("/profile", GetProfileAsync)
            .WithName("GetProfile")
            .Produces<AthleteProfileResponse>()
            .Produces(StatusCodes.Status404NotFound);
        group.MapPut("/profile", UpdateProfileAsync)
            .WithName("UpdateProfile")
            .Produces<AthleteProfileResponse>()
            .ProducesValidationProblem();
        group.MapGet("/health-contexts", GetHealthContextsAsync)
            .WithName("GetHealthContexts")
            .Produces<IReadOnlyList<HealthContextResponse>>();
        group.MapPost("/health-contexts", CreateHealthContextAsync)
            .WithName("CreateHealthContext")
            .Produces<HealthContextResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();
        group.MapPut("/health-contexts/{id:guid}", UpdateHealthContextAsync)
            .WithName("UpdateHealthContext")
            .Produces<HealthContextResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> GetProfileAsync(
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var profile = await ReadProfileAsync(session, cancellationToken);
        await session.CommitAsync(cancellationToken);
        return profile is null ? Results.NotFound() : Results.Ok(profile);
    }

    private static async Task<IResult> UpdateProfileAsync(
        UpdateAthleteProfileRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var errors = RequestValidation.Profile(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                insert into app.athlete_profiles (
                  owner_id,
                  display_name,
                  birth_date,
                  height_cm,
                  weight_kg,
                  sex,
                  timezone_name,
                  locale,
                  unit_system)
                values (
                  @owner_id,
                  @display_name,
                  @birth_date,
                  @height_cm,
                  @weight_kg,
                  @sex,
                  @timezone_name,
                  @locale,
                  @unit_system)
                on conflict (owner_id) do update
                set display_name = excluded.display_name,
                    birth_date = excluded.birth_date,
                    height_cm = excluded.height_cm,
                    weight_kg = excluded.weight_kg,
                    sex = excluded.sex,
                    timezone_name = excluded.timezone_name,
                    locale = excluded.locale,
                    unit_system = excluded.unit_system;
                """;
            command.Parameters.AddWithValue("owner_id", ownerId);
            command.Parameters.AddWithValue("display_name", request.DisplayName.Trim());
            AddNullable(command, "birth_date", NpgsqlDbType.Date, request.BirthDate);
            AddNullable(command, "height_cm", NpgsqlDbType.Numeric, request.HeightCm);
            AddNullable(command, "weight_kg", NpgsqlDbType.Numeric, request.WeightKg);
            command.Parameters.AddWithValue("sex", request.Sex);
            command.Parameters.AddWithValue("timezone_name", request.TimezoneName.Trim());
            command.Parameters.AddWithValue("locale", request.Locale.Trim());
            command.Parameters.AddWithValue("unit_system", request.UnitSystem);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditWriter.WriteAsync(
            session,
            ownerId,
            "profile.saved",
            "athlete_profile",
            ownerId,
            httpContext.GetCorrelationId(),
            ["display_name", "birth_date", "height_cm", "weight_kg", "sex", "timezone_name", "locale", "unit_system"],
            cancellationToken);

        var profile = await ReadProfileAsync(session, cancellationToken)
            ?? throw new InvalidOperationException("Profile upsert did not return a row.");
        await session.CommitAsync(cancellationToken);
        return Results.Ok(profile);
    }

    private static async Task<IResult> GetHealthContextsAsync(
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var contexts = new List<HealthContextResponse>();
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select id, context_type, body_location, started_on, ended_on, status, description, updated_at
                from app.athlete_health_contexts
                order by
                  case status when 'active' then 0 when 'monitoring' then 1 else 2 end,
                  started_on desc nulls last,
                  created_at desc;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                contexts.Add(ReadHealthContext(reader));
            }
        }

        await session.CommitAsync(cancellationToken);
        return Results.Ok(contexts);
    }

    private static async Task<IResult> CreateHealthContextAsync(
        SaveHealthContextRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var errors = RequestValidation.Health(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var created = await SaveHealthContextAsync(session, ownerId, null, request, cancellationToken)
            ?? throw new InvalidOperationException("Health context insert did not return a row.");
        await AuditWriter.WriteAsync(
            session,
            ownerId,
            "health_context.created",
            "athlete_health_context",
            created.Id,
            httpContext.GetCorrelationId(),
            ["context_type", "body_location", "started_on", "ended_on", "status", "description"],
            cancellationToken);
        await session.CommitAsync(cancellationToken);
        return Results.Created($"/api/v1/health-contexts/{created.Id}", created);
    }

    private static async Task<IResult> UpdateHealthContextAsync(
        Guid id,
        SaveHealthContextRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var errors = RequestValidation.Health(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var updated = await SaveHealthContextAsync(session, ownerId, id, request, cancellationToken);
        if (updated is null)
        {
            return Results.NotFound();
        }

        await AuditWriter.WriteAsync(
            session,
            ownerId,
            "health_context.updated",
            "athlete_health_context",
            id,
            httpContext.GetCorrelationId(),
            ["context_type", "body_location", "started_on", "ended_on", "status", "description"],
            cancellationToken);
        await session.CommitAsync(cancellationToken);
        return Results.Ok(updated);
    }

    private static async Task<AthleteProfileResponse?> ReadProfileAsync(
        OwnerDbSession session,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            select display_name, birth_date, height_cm, weight_kg, sex, timezone_name, locale, unit_system, updated_at
            from app.athlete_profiles;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AthleteProfileResponse(
            reader.GetString(0),
            GetNullable<DateOnly>(reader, 1),
            GetNullable<decimal>(reader, 2),
            GetNullable<decimal>(reader, 3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetDateTime(8));
    }

    private static async Task<HealthContextResponse?> SaveHealthContextAsync(
        OwnerDbSession session,
        Guid ownerId,
        Guid? id,
        SaveHealthContextRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = id is null
            ? """
                insert into app.athlete_health_contexts (
                  owner_id, context_type, body_location, started_on, ended_on, status, description)
                values (
                  @owner_id, @context_type, @body_location, @started_on, @ended_on, @status, @description)
                returning id, context_type, body_location, started_on, ended_on, status, description, updated_at;
                """
            : """
                update app.athlete_health_contexts
                set context_type = @context_type,
                    body_location = @body_location,
                    started_on = @started_on,
                    ended_on = @ended_on,
                    status = @status,
                    description = @description
                where id = @id
                returning id, context_type, body_location, started_on, ended_on, status, description, updated_at;
                """;
        command.Parameters.AddWithValue("owner_id", ownerId);
        if (id.HasValue)
        {
            command.Parameters.AddWithValue("id", id.Value);
        }

        command.Parameters.AddWithValue("context_type", request.ContextType);
        AddNullable(command, "body_location", NpgsqlDbType.Text, CleanOptional(request.BodyLocation));
        AddNullable(command, "started_on", NpgsqlDbType.Date, request.StartedOn);
        AddNullable(command, "ended_on", NpgsqlDbType.Date, request.EndedOn);
        command.Parameters.AddWithValue("status", request.Status);
        command.Parameters.AddWithValue("description", request.Description.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadHealthContext(reader) : null;
    }

    private static HealthContextResponse ReadHealthContext(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            GetNullableString(reader, 2),
            GetNullable<DateOnly>(reader, 3),
            GetNullable<DateOnly>(reader, 4),
            reader.GetString(5),
            reader.GetString(6),
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
