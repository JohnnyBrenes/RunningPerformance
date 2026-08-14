using System.Security.Claims;
using Npgsql;
using NpgsqlTypes;
using RunningPerformance.Api.Authentication;
using RunningPerformance.Infrastructure.Database;

namespace RunningPerformance.Api.Features;

public static class ExerciseEndpoints
{
    public static IEndpointRouteBuilder MapExerciseEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/exercises").WithTags("Exercises");

        group.MapGet("/", GetExercisesAsync)
            .WithName("GetExercises")
            .Produces<IReadOnlyList<ExerciseResponse>>();
        group.MapGet("/{id:guid}", GetExerciseAsync)
            .WithName("GetExercise")
            .Produces<ExerciseResponse>()
            .Produces(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> GetExercisesAsync(
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var exercises = await ExerciseQueries.ReadCurrentAsync(session, null, cancellationToken);
        await session.CommitAsync(cancellationToken);
        return Results.Ok(exercises);
    }

    private static async Task<IResult> GetExerciseAsync(
        Guid id,
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var exercise = (await ExerciseQueries.ReadCurrentAsync(session, id, cancellationToken)).SingleOrDefault();
        await session.CommitAsync(cancellationToken);
        return exercise is null ? Results.NotFound() : Results.Ok(exercise);
    }
}

internal static class ExerciseQueries
{
    public static async Task<IReadOnlyList<ExerciseResponse>> ReadCurrentAsync(
        OwnerDbSession session,
        Guid? exerciseId,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            with current_revisions as (
              select distinct on (r.exercise_id) r.*
              from app.exercise_revisions r
              order by r.exercise_id, r.version_number desc
            )
            select
              e.id, e.slug, e.canonical_name, e.movement_pattern, e.equipment, e.status,
              r.id, r.version_number, r.display_name, r.brief_description,
              r.setup, r.execution, r.safety_cues,
              m.id, m.position, m.asset_uri, m.alt_text, m.mime_type, m.source,
              m.author, m.license, m.sha256, m.presentation_sex, m.width_px, m.height_px
            from app.exercises e
            join current_revisions r on r.owner_id = e.owner_id and r.exercise_id = e.id
            left join app.exercise_media m
              on m.owner_id = r.owner_id and m.exercise_revision_id = r.id
            where e.status = 'active' and (@exercise_id is null or e.id = @exercise_id)
            order by e.canonical_name, e.id, m.position;
            """;
        command.Parameters.Add("exercise_id", NpgsqlDbType.Uuid).Value =
            exerciseId.HasValue ? exerciseId.Value : DBNull.Value;
        return await ReadAsync(command, cancellationToken);
    }

    public static async Task<IReadOnlyDictionary<Guid, ExerciseResponse>> ReadByRevisionIdsAsync(
        OwnerDbSession session,
        IReadOnlyCollection<Guid> revisionIds,
        CancellationToken cancellationToken)
    {
        if (revisionIds.Count == 0)
        {
            return new Dictionary<Guid, ExerciseResponse>();
        }

        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            select
              e.id, e.slug, e.canonical_name, e.movement_pattern, e.equipment, e.status,
              r.id, r.version_number, r.display_name, r.brief_description,
              r.setup, r.execution, r.safety_cues,
              m.id, m.position, m.asset_uri, m.alt_text, m.mime_type, m.source,
              m.author, m.license, m.sha256, m.presentation_sex, m.width_px, m.height_px
            from app.exercise_revisions r
            join app.exercises e on e.owner_id = r.owner_id and e.id = r.exercise_id
            left join app.exercise_media m
              on m.owner_id = r.owner_id and m.exercise_revision_id = r.id
            where r.id = any(@revision_ids)
            order by e.canonical_name, e.id, m.position;
            """;
        command.Parameters.AddWithValue("revision_ids", revisionIds.ToArray());
        var exercises = await ReadAsync(command, cancellationToken);
        return exercises.ToDictionary(exercise => exercise.Revision.Id);
    }

    private static async Task<IReadOnlyList<ExerciseResponse>> ReadAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var order = new List<Guid>();
        var builders = new Dictionary<Guid, ExerciseBuilder>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var revisionId = reader.GetGuid(6);
            if (!builders.TryGetValue(revisionId, out var builder))
            {
                builder = new ExerciseBuilder(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    NullableString(reader, 3),
                    NullableString(reader, 4),
                    reader.GetString(5),
                    revisionId,
                    reader.GetInt32(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.GetString(10),
                    reader.GetString(11),
                    reader.GetString(12));
                builders.Add(revisionId, builder);
                order.Add(revisionId);
            }

            if (!reader.IsDBNull(13))
            {
                builder.Media.Add(new ExerciseMediaResponse(
                    reader.GetGuid(13),
                    reader.GetInt16(14),
                    reader.GetString(15),
                    reader.GetString(16),
                    reader.GetString(17),
                    reader.GetString(18),
                    NullableString(reader, 19),
                    reader.GetString(20),
                    NullableString(reader, 21)?.Trim(),
                    reader.GetString(22),
                    reader.GetInt32(23),
                    reader.GetInt32(24)));
            }
        }

        return order.Select(id => builders[id].Build()).ToArray();
    }

    private static string? NullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private sealed class ExerciseBuilder(
        Guid id,
        string slug,
        string canonicalName,
        string? movementPattern,
        string? equipment,
        string status,
        Guid revisionId,
        int versionNumber,
        string displayName,
        string briefDescription,
        string setup,
        string execution,
        string safetyCues)
    {
        public List<ExerciseMediaResponse> Media { get; } = [];

        public ExerciseResponse Build() => new(
            id,
            slug,
            canonicalName,
            movementPattern,
            equipment,
            status,
            new ExerciseRevisionResponse(
                revisionId,
                versionNumber,
                displayName,
                briefDescription,
                setup,
                execution,
                safetyCues,
                Media));
    }
}
