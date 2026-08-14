using System.Security.Claims;
using Npgsql;
using NpgsqlTypes;
using RunningPerformance.Api.Authentication;
using RunningPerformance.Api.Http;
using RunningPerformance.Infrastructure.Database;

namespace RunningPerformance.Api.Features;

public static class TrainingPlanEndpoints
{
    public static IEndpointRouteBuilder MapTrainingPlanEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/plans").WithTags("Plans");

        group.MapGet("/", GetPlansAsync)
            .WithName("GetTrainingPlans")
            .Produces<IReadOnlyList<TrainingPlanSummaryResponse>>();
        group.MapGet("/current", GetCurrentPlanAsync)
            .WithName("GetCurrentTrainingPlan")
            .Produces<TrainingPlanDetailResponse>()
            .Produces(StatusCodes.Status404NotFound);
        group.MapGet("/{planId:guid}/versions/{versionId:guid}", GetPlanVersionAsync)
            .WithName("GetTrainingPlanVersion")
            .Produces<TrainingPlanDetailResponse>()
            .Produces(StatusCodes.Status404NotFound);
        group.MapPost("/{planId:guid}/drafts", CloneDraftAsync)
            .WithName("CloneTrainingPlanDraft")
            .Produces<TrainingPlanDetailResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        group.MapPost("/{planId:guid}/versions/{versionId:guid}/publish", PublishAsync)
            .WithName("PublishTrainingPlanVersion")
            .Produces<TrainingPlanDetailResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        group.MapPut("/{planId:guid}/versions/{versionId:guid}/sessions/{sessionId:guid}", UpdateSessionAsync)
            .WithName("UpdatePlannedSession")
            .Produces<TrainingPlanDetailResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return routes;
    }

    private static async Task<IResult> GetPlansAsync(
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var plans = await ReadPlanSummariesAsync(session, cancellationToken);
        await session.CommitAsync(cancellationToken);
        return Results.Ok(plans);
    }

    private static async Task<IResult> GetCurrentPlanAsync(
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var plan = await ReadPlanDetailAsync(session, null, null, true, cancellationToken);
        await session.CommitAsync(cancellationToken);
        return plan is null ? Results.NotFound() : Results.Ok(plan);
    }

    private static async Task<IResult> GetPlanVersionAsync(
        Guid planId,
        Guid versionId,
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var plan = await ReadPlanDetailAsync(session, planId, versionId, false, cancellationToken);
        await session.CommitAsync(cancellationToken);
        return plan is null ? Results.NotFound() : Results.Ok(plan);
    }

    private static async Task<IResult> CloneDraftAsync(
        Guid planId,
        CloneTrainingPlanDraftRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var errors = RequestValidation.PlanDraft(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        Guid versionId;
        try
        {
            await using var command = session.Connection.CreateCommand();
            command.Transaction = session.Transaction;
            command.CommandText = """
                select (app.clone_training_plan_draft(
                  @plan_id, @source_version_id, @rationale, @correlation_id)).id;
                """;
            command.Parameters.AddWithValue("plan_id", planId);
            command.Parameters.AddWithValue("source_version_id", request.SourceVersionId);
            command.Parameters.AddWithValue("rationale", request.Rationale.Trim());
            command.Parameters.AddWithValue("correlation_id", httpContext.GetCorrelationId());
            versionId = (Guid)(await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Draft clone did not return a version."));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.NoDataFound)
        {
            return Results.NotFound();
        }
        catch (PostgresException exception) when (exception.SqlState == "55000")
        {
            return Results.Problem(
                title: "Ya existe un borrador",
                detail: "Publica o descarta el borrador existente antes de crear otro.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var plan = await ReadPlanDetailAsync(session, planId, versionId, false, cancellationToken)
            ?? throw new InvalidOperationException("Cloned draft could not be read.");
        await session.CommitAsync(cancellationToken);
        return Results.Created($"/api/v1/plans/{planId}/versions/{versionId}", plan);
    }

    private static async Task<IResult> PublishAsync(
        Guid planId,
        Guid versionId,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        try
        {
            await using var command = session.Connection.CreateCommand();
            command.Transaction = session.Transaction;
            command.CommandText = """
                select (app.publish_training_plan_version(@version_id, @correlation_id)).id;
                """;
            command.Parameters.AddWithValue("version_id", versionId);
            command.Parameters.AddWithValue("correlation_id", httpContext.GetCorrelationId());
            await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.NoDataFound)
        {
            return Results.NotFound();
        }
        catch (PostgresException exception) when (exception.SqlState is "55000" or PostgresErrorCodes.CheckViolation)
        {
            return Results.Problem(
                title: "La versión no se puede publicar",
                detail: exception.MessageText,
                statusCode: StatusCodes.Status409Conflict);
        }

        var plan = await ReadPlanDetailAsync(session, planId, versionId, false, cancellationToken);
        if (plan is null)
        {
            return Results.NotFound();
        }

        await session.CommitAsync(cancellationToken);
        return Results.Ok(plan);
    }

    private static async Task<IResult> UpdateSessionAsync(
        Guid planId,
        Guid versionId,
        Guid sessionId,
        UpdatePlannedSessionRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var errors = RequestValidation.PlannedSession(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        string? versionStatus;
        await using (var statusCommand = session.Connection.CreateCommand())
        {
            statusCommand.Transaction = session.Transaction;
            statusCommand.CommandText = """
                select status from app.training_plan_versions
                where id = @version_id and training_plan_id = @plan_id;
                """;
            statusCommand.Parameters.AddWithValue("version_id", versionId);
            statusCommand.Parameters.AddWithValue("plan_id", planId);
            versionStatus = (string?)await statusCommand.ExecuteScalarAsync(cancellationToken);
        }

        if (versionStatus is null)
        {
            return Results.NotFound();
        }
        if (versionStatus != "draft")
        {
            return Results.Problem(
                title: "La versión publicada es inmutable",
                detail: "Crea un nuevo borrador para modificar una sesión.",
                statusCode: StatusCodes.Status409Conflict);
        }

        int changed;
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                update app.planned_sessions s
                set scheduled_date = @scheduled_date,
                    objective = @objective
                from app.training_plan_versions v
                where s.id = @session_id
                  and s.training_plan_version_id = v.id
                  and v.id = @version_id
                  and v.training_plan_id = @plan_id
                  and @scheduled_date between v.period_start and v.period_end;
                """;
            command.Parameters.AddWithValue("session_id", sessionId);
            command.Parameters.AddWithValue("version_id", versionId);
            command.Parameters.AddWithValue("plan_id", planId);
            command.Parameters.AddWithValue("scheduled_date", request.ScheduledDate);
            command.Parameters.AddWithValue("objective", request.Objective.Trim());
            changed = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (changed == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.ScheduledDate)] = ["La fecha debe pertenecer al periodo del plan."]
            });
        }

        await AuditWriter.WriteAsync(
            session,
            ownerId,
            "training_plan.session_updated",
            "planned_session",
            sessionId,
            httpContext.GetCorrelationId(),
            ["scheduled_date", "objective"],
            cancellationToken);

        var plan = await ReadPlanDetailAsync(session, planId, versionId, false, cancellationToken)
            ?? throw new InvalidOperationException("Updated plan could not be read.");
        await session.CommitAsync(cancellationToken);
        return Results.Ok(plan);
    }

    private static async Task<IReadOnlyList<TrainingPlanSummaryResponse>> ReadPlanSummariesAsync(
        OwnerDbSession session,
        CancellationToken cancellationToken)
    {
        var order = new List<Guid>();
        var plans = new Dictionary<Guid, PlanSummaryBuilder>();
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            select
              p.id, p.name, p.purpose, p.target_start, p.target_end, p.status,
              v.id, v.version_number, v.period_start, v.period_end, v.status,
              v.rationale, v.supersedes_id, v.published_at, v.created_at,
              count(s.id)::integer
            from app.training_plans p
            left join app.training_plan_versions v
              on v.owner_id = p.owner_id and v.training_plan_id = p.id
            left join app.planned_sessions s
              on s.owner_id = v.owner_id and s.training_plan_version_id = v.id
            group by p.id, v.id
            order by p.created_at, p.id, v.version_number desc;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var planId = reader.GetGuid(0);
            if (!plans.TryGetValue(planId, out var builder))
            {
                builder = new PlanSummaryBuilder(
                    planId,
                    reader.GetString(1),
                    reader.GetString(2),
                    Nullable<DateOnly>(reader, 3),
                    Nullable<DateOnly>(reader, 4),
                    reader.GetString(5));
                plans.Add(planId, builder);
                order.Add(planId);
            }

            if (!reader.IsDBNull(6))
            {
                builder.Versions.Add(ReadVersion(reader, 6));
            }
        }

        return order.Select(id => plans[id].Build()).ToArray();
    }

    private static async Task<TrainingPlanDetailResponse?> ReadPlanDetailAsync(
        OwnerDbSession session,
        Guid? planId,
        Guid? versionId,
        bool current,
        CancellationToken cancellationToken)
    {
        Guid resolvedPlanId;
        string name;
        string purpose;
        string planStatus;
        TrainingPlanVersionSummaryResponse version;

        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select
                  p.id, p.name, p.purpose, p.status,
                  v.id, v.version_number, v.period_start, v.period_end, v.status,
                  v.rationale, v.supersedes_id, v.published_at, v.created_at,
                  (select count(*)::integer from app.planned_sessions s
                   where s.owner_id = v.owner_id and s.training_plan_version_id = v.id)
                from app.training_plans p
                join app.training_plan_versions v
                  on v.owner_id = p.owner_id and v.training_plan_id = p.id
                where (@current and v.status = 'published')
                   or (not @current and p.id = @plan_id and v.id = @version_id)
                limit 1;
                """;
            command.Parameters.AddWithValue("current", current);
            command.Parameters.Add("plan_id", NpgsqlDbType.Uuid).Value =
                planId.HasValue ? planId.Value : DBNull.Value;
            command.Parameters.Add("version_id", NpgsqlDbType.Uuid).Value =
                versionId.HasValue ? versionId.Value : DBNull.Value;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            resolvedPlanId = reader.GetGuid(0);
            name = reader.GetString(1);
            purpose = reader.GetString(2);
            planStatus = reader.GetString(3);
            version = ReadVersion(reader, 4);
        }

        var sessions = new Dictionary<Guid, SessionBuilder>();
        var sessionOrder = new List<Guid>();
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select id, scheduled_date, session_type, modality, obligation, objective,
                  distance_m, duration_seconds, target_rpe_min, target_rpe_max, terrain,
                  warmup, main_set, recoveries, cooldown
                from app.planned_sessions
                where training_plan_version_id = @version_id
                order by scheduled_date, id;
                """;
            command.Parameters.AddWithValue("version_id", version.Id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetGuid(0);
                sessions.Add(id, new SessionBuilder(
                    id,
                    reader.GetFieldValue<DateOnly>(1),
                    reader.GetString(2),
                    NullableString(reader, 3),
                    reader.GetString(4),
                    reader.GetString(5),
                    Nullable<decimal>(reader, 6),
                    Nullable<decimal>(reader, 7),
                    Nullable<decimal>(reader, 8),
                    Nullable<decimal>(reader, 9),
                    NullableString(reader, 10),
                    NullableString(reader, 11),
                    NullableString(reader, 12),
                    NullableString(reader, 13),
                    NullableString(reader, 14)));
                sessionOrder.Add(id);
            }
        }

        var blocks = new Dictionary<Guid, BlockBuilder>();
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select b.id, b.planned_session_id, b.position, b.block_type,
                  b.repeat_count, b.instructions
                from app.planned_session_blocks b
                join app.planned_sessions s
                  on s.owner_id = b.owner_id and s.id = b.planned_session_id
                where s.training_plan_version_id = @version_id
                order by s.scheduled_date, b.position;
                """;
            command.Parameters.AddWithValue("version_id", version.Id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var block = new BlockBuilder(
                    reader.GetGuid(0),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetString(5));
                blocks.Add(block.Id, block);
                if (sessions.TryGetValue(reader.GetGuid(1), out var parent))
                {
                    parent.Blocks.Add(block);
                }
            }
        }

        var revisionIds = new HashSet<Guid>();
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select e.id, e.planned_session_block_id, e.exercise_revision_id,
                  e.position, e.sets, e.repetitions_min, e.repetitions_max,
                  e.duration_seconds, e.rest_seconds, e.load_value, e.load_unit,
                  e.target_rpe, e.target_rir, e.tempo, e.side, e.note
                from app.planned_session_exercises e
                join app.planned_session_blocks b
                  on b.owner_id = e.owner_id and b.id = e.planned_session_block_id
                join app.planned_sessions s
                  on s.owner_id = b.owner_id and s.id = b.planned_session_id
                where s.training_plan_version_id = @version_id
                order by b.position, e.position;
                """;
            command.Parameters.AddWithValue("version_id", version.Id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var revisionId = reader.GetGuid(2);
                revisionIds.Add(revisionId);
                if (blocks.TryGetValue(reader.GetGuid(1), out var block))
                {
                    block.Exercises.Add(new PlannedExerciseBuilder(
                        reader.GetGuid(0),
                        revisionId,
                        reader.GetInt32(3),
                        Nullable<int>(reader, 4),
                        Nullable<int>(reader, 5),
                        Nullable<int>(reader, 6),
                        Nullable<decimal>(reader, 7),
                        Nullable<decimal>(reader, 8),
                        Nullable<decimal>(reader, 9),
                        NullableString(reader, 10),
                        Nullable<decimal>(reader, 11),
                        Nullable<decimal>(reader, 12),
                        NullableString(reader, 13),
                        NullableString(reader, 14),
                        NullableString(reader, 15)));
                }
            }
        }

        var exercises = await ExerciseQueries.ReadByRevisionIdsAsync(
            session,
            revisionIds,
            cancellationToken);
        return new TrainingPlanDetailResponse(
            resolvedPlanId,
            name,
            purpose,
            planStatus,
            version,
            sessionOrder.Select(id => sessions[id].Build(exercises)).ToArray());
    }

    private static TrainingPlanVersionSummaryResponse ReadVersion(NpgsqlDataReader reader, int offset) =>
        new(
            reader.GetGuid(offset),
            reader.GetInt32(offset + 1),
            reader.GetFieldValue<DateOnly>(offset + 2),
            reader.GetFieldValue<DateOnly>(offset + 3),
            reader.GetString(offset + 4),
            reader.GetString(offset + 5),
            Nullable<Guid>(reader, offset + 6),
            Nullable<DateTime>(reader, offset + 7),
            reader.GetDateTime(offset + 8),
            reader.GetInt32(offset + 9));

    private static T? Nullable<T>(NpgsqlDataReader reader, int ordinal) where T : struct =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<T>(ordinal);

    private static string? NullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private sealed class PlanSummaryBuilder(
        Guid id,
        string name,
        string purpose,
        DateOnly? targetStart,
        DateOnly? targetEnd,
        string status)
    {
        public List<TrainingPlanVersionSummaryResponse> Versions { get; } = [];

        public TrainingPlanSummaryResponse Build() =>
            new(id, name, purpose, targetStart, targetEnd, status, Versions);
    }

    private sealed class SessionBuilder(
        Guid id,
        DateOnly scheduledDate,
        string sessionType,
        string? modality,
        string obligation,
        string objective,
        decimal? distanceM,
        decimal? durationSeconds,
        decimal? targetRpeMin,
        decimal? targetRpeMax,
        string? terrain,
        string? warmup,
        string? mainSet,
        string? recoveries,
        string? cooldown)
    {
        public List<BlockBuilder> Blocks { get; } = [];

        public PlannedSessionResponse Build(IReadOnlyDictionary<Guid, ExerciseResponse> exercises) =>
            new(
                id,
                scheduledDate,
                sessionType,
                modality,
                obligation,
                objective,
                distanceM,
                durationSeconds,
                targetRpeMin,
                targetRpeMax,
                terrain,
                warmup,
                mainSet,
                recoveries,
                cooldown,
                Blocks.Select(block => block.Build(exercises)).ToArray());
    }

    private sealed class BlockBuilder(
        Guid id,
        int position,
        string blockType,
        int repeatCount,
        string instructions)
    {
        public Guid Id { get; } = id;

        public List<PlannedExerciseBuilder> Exercises { get; } = [];

        public PlannedSessionBlockResponse Build(IReadOnlyDictionary<Guid, ExerciseResponse> exercises) =>
            new(
                Id,
                position,
                blockType,
                repeatCount,
                instructions,
                Exercises.Select(exercise => exercise.Build(exercises[exercise.RevisionId])).ToArray());
    }

    private sealed record PlannedExerciseBuilder(
        Guid Id,
        Guid RevisionId,
        int Position,
        int? Sets,
        int? RepetitionsMin,
        int? RepetitionsMax,
        decimal? DurationSeconds,
        decimal? RestSeconds,
        decimal? LoadValue,
        string? LoadUnit,
        decimal? TargetRpe,
        decimal? TargetRir,
        string? Tempo,
        string? Side,
        string? Note)
    {
        public PlannedExerciseResponse Build(ExerciseResponse exercise) =>
            new(
                Id,
                Position,
                Sets,
                RepetitionsMin,
                RepetitionsMax,
                DurationSeconds,
                RestSeconds,
                LoadValue,
                LoadUnit,
                TargetRpe,
                TargetRir,
                Tempo,
                Side,
                Note,
                exercise);
    }
}
