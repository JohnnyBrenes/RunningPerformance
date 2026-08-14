using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;
using RunningPerformance.Api.Authentication;
using RunningPerformance.Api.Http;
using RunningPerformance.Application.Evaluations;
using RunningPerformance.Infrastructure.Database;

namespace RunningPerformance.Api.Features;

public static class WeeklyEvaluationEndpoints
{
    public static IEndpointRouteBuilder MapWeeklyEvaluationEndpoints(
        this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/evaluations").WithTags("Evaluations");

        group.MapGet("/", ListAsync)
            .WithName("GetWeeklyEvaluations")
            .Produces<IReadOnlyList<WeeklyEvaluationSummaryResponse>>();
        group.MapGet("/{evaluationId:guid}", GetAsync)
            .WithName("GetWeeklyEvaluation")
            .Produces<WeeklyEvaluationDetailResponse>()
            .Produces(StatusCodes.Status404NotFound);
        group.MapPost("/snapshots", CreateSnapshotAsync)
            .WithName("CreateWeeklyEvaluationSnapshot")
            .Produces<WeeklyEvaluationDetailResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        group.MapPost("/{evaluationId:guid}/decisions", ConfirmDecisionAsync)
            .WithName("ConfirmWeeklyDecision")
            .Produces<WeeklyEvaluationDetailResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return routes;
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var evaluations = new List<WeeklyEvaluationSummaryResponse>();
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select e.id, e.week_start, e.week_end, e.format_version,
                  e.plan_version_id, e.cutoff_at, e.status, e.traffic_light,
                  e.rationale, e.created_at, (d.id is not null)
                from app.weekly_evaluations e
                left join app.weekly_decisions d
                  on d.owner_id = e.owner_id and d.weekly_evaluation_id = e.id
                order by e.week_start desc, e.created_at desc;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                evaluations.Add(ReadSummary(reader));
            }
        }

        await session.CommitAsync(cancellationToken);
        return Results.Ok(evaluations);
    }

    private static async Task<IResult> GetAsync(
        Guid evaluationId,
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var evaluation = await ReadDetailAsync(session, evaluationId, cancellationToken);
        await session.CommitAsync(cancellationToken);
        return evaluation is null ? Results.NotFound() : Results.Ok(evaluation);
    }

    private static async Task<IResult> CreateSnapshotAsync(
        CreateWeeklyEvaluationSnapshotRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var errors = ValidateSnapshot(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        Guid evaluationId;
        try
        {
            await using var command = session.Connection.CreateCommand();
            command.Transaction = session.Transaction;
            command.CommandText = """
                select app.create_weekly_evaluation_snapshot(
                  @week_start, @status, @correlation_id);
                """;
            command.Parameters.AddWithValue("week_start", request.WeekStart);
            command.Parameters.AddWithValue("status", request.Status);
            command.Parameters.AddWithValue("correlation_id", httpContext.GetCorrelationId());
            evaluationId = (Guid)(await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Snapshot creation returned no identifier."));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.NoDataFound)
        {
            return Results.NotFound(new ProblemDetails
            {
                Title = "No hay un plan para la semana",
                Detail = exception.MessageText,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (PostgresException exception) when (
            exception.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation)
        {
            return Results.Problem(
                title: "La evaluación no se puede crear",
                detail: exception.SqlState == PostgresErrorCodes.UniqueViolation
                    ? "Ya existe un cierre final para esa semana. Crea un snapshot provisional o consulta el cierre existente."
                    : exception.MessageText,
                statusCode: StatusCodes.Status409Conflict);
        }

        var detail = await ReadDetailAsync(session, evaluationId, cancellationToken)
            ?? throw new InvalidOperationException("Created snapshot could not be read.");
        await session.CommitAsync(cancellationToken);
        return Results.Created($"/api/v1/evaluations/{evaluationId}", detail);
    }

    private static async Task<IResult> ConfirmDecisionAsync(
        Guid evaluationId,
        ConfirmWeeklyDecisionRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var errors = ValidateDecision(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        Guid? evaluationPlanVersionId;
        await using (var evaluationCommand = session.Connection.CreateCommand())
        {
            evaluationCommand.Transaction = session.Transaction;
            evaluationCommand.CommandText = """
                select plan_version_id
                from app.weekly_evaluations
                where id = @evaluation_id;
                """;
            evaluationCommand.Parameters.AddWithValue("evaluation_id", evaluationId);
            evaluationPlanVersionId = (Guid?)await evaluationCommand.ExecuteScalarAsync(cancellationToken);
        }

        if (evaluationPlanVersionId is null)
        {
            return Results.NotFound();
        }

        if (request.PlanAdjustment is not null
            && request.PlanAdjustment.SourcePlanVersionId != evaluationPlanVersionId)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["planAdjustment.sourcePlanVersionId"] =
                    ["La versión de origen debe ser la congelada en la evaluación."]
            });
        }

        Guid decisionId;
        try
        {
            await using var command = session.Connection.CreateCommand();
            command.Transaction = session.Transaction;
            command.CommandText = """
                insert into app.weekly_decisions (
                  owner_id, weekly_evaluation_id, decision, observation,
                  evidence, historical_comparison, interpretation,
                  recommendation, confirmed_by)
                values (
                  @owner_id, @evaluation_id, @decision, @observation,
                  @evidence, @historical_comparison, @interpretation,
                  @recommendation, @owner_id)
                returning id;
                """;
            command.Parameters.AddWithValue("owner_id", ownerId);
            command.Parameters.AddWithValue("evaluation_id", evaluationId);
            command.Parameters.AddWithValue("decision", request.Decision);
            command.Parameters.AddWithValue("observation", request.Observation.Trim());
            command.Parameters.AddWithValue("evidence", request.Evidence.Trim());
            command.Parameters.AddWithValue(
                "historical_comparison", request.HistoricalComparison.Trim());
            command.Parameters.AddWithValue("interpretation", request.Interpretation.Trim());
            command.Parameters.AddWithValue("recommendation", request.Recommendation.Trim());
            decisionId = (Guid)(await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Decision creation returned no identifier."));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Results.Problem(
                title: "La evaluación ya tiene una decisión",
                detail: "Las decisiones confirmadas son append-only; crea un nuevo snapshot si necesitas reevaluar.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (request.PlanAdjustment is not null)
        {
            try
            {
                await ApplyPlanAdjustmentAsync(
                    session,
                    ownerId,
                    decisionId,
                    request.PlanAdjustment,
                    httpContext.GetCorrelationId(),
                    cancellationToken);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.NoDataFound)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Title = "No se encontró la versión o sesión de origen",
                    Detail = exception.MessageText,
                    Status = StatusCodes.Status404NotFound
                });
            }
            catch (PlanAdjustmentNotFoundException exception)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Title = "No se encontró la versión o sesión de origen",
                    Detail = exception.Message,
                    Status = StatusCodes.Status404NotFound
                });
            }
            catch (PostgresException exception) when (
                exception.SqlState is "55000" or PostgresErrorCodes.CheckViolation)
            {
                return Results.Problem(
                    title: "No se pudo crear la nueva versión del plan",
                    detail: exception.MessageText,
                    statusCode: StatusCodes.Status409Conflict);
            }
        }

        await AuditWriter.WriteAsync(
            session,
            ownerId,
            "weekly_decision.confirmed",
            "weekly_decision",
            decisionId,
            httpContext.GetCorrelationId(),
            ["decision", "narrative", "plan_adjustment"],
            cancellationToken);

        var detail = await ReadDetailAsync(session, evaluationId, cancellationToken)
            ?? throw new InvalidOperationException("Decided evaluation could not be read.");
        await session.CommitAsync(cancellationToken);
        return Results.Created($"/api/v1/evaluations/{evaluationId}", detail);
    }

    private static async Task ApplyPlanAdjustmentAsync(
        OwnerDbSession session,
        Guid ownerId,
        Guid decisionId,
        PlanVersionAdjustmentRequest request,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        Guid planId;
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select training_plan_id
                from app.training_plan_versions
                where id = @source_version_id;
                """;
            command.Parameters.AddWithValue("source_version_id", request.SourcePlanVersionId);
            planId = (Guid)(await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new PlanAdjustmentNotFoundException(
                    "Training plan version not found."));
        }

        Guid targetVersionId;
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select (app.clone_training_plan_draft(
                  @plan_id, @source_version_id, @rationale, @correlation_id)).id;
                """;
            command.Parameters.AddWithValue("plan_id", planId);
            command.Parameters.AddWithValue("source_version_id", request.SourcePlanVersionId);
            command.Parameters.AddWithValue("rationale", request.Rationale.Trim());
            command.Parameters.AddWithValue("correlation_id", correlationId);
            targetVersionId = (Guid)(await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Draft clone returned no identifier."));
        }

        foreach (var change in request.SessionChanges)
        {
            Guid targetSessionId;
            DateOnly beforeDate;
            string beforeObjective;
            await using (var command = session.Connection.CreateCommand())
            {
                command.Transaction = session.Transaction;
                command.CommandText = """
                    with source_sessions as (
                      select id, scheduled_date, objective,
                        row_number() over (order by scheduled_date, id) as position
                      from app.planned_sessions
                      where training_plan_version_id = @source_version_id
                    ), target_sessions as (
                      select id,
                        row_number() over (order by scheduled_date, id) as position
                      from app.planned_sessions
                      where training_plan_version_id = @target_version_id
                    )
                    select target.id, source.scheduled_date, source.objective
                    from source_sessions source
                    join target_sessions target using (position)
                    where source.id = @source_session_id;
                    """;
                command.Parameters.AddWithValue("source_version_id", request.SourcePlanVersionId);
                command.Parameters.AddWithValue("target_version_id", targetVersionId);
                command.Parameters.AddWithValue("source_session_id", change.SourcePlannedSessionId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new PlanAdjustmentNotFoundException(
                        "Planned session not found in source version.");
                }

                targetSessionId = reader.GetGuid(0);
                beforeDate = reader.GetFieldValue<DateOnly>(1);
                beforeObjective = reader.GetString(2);
            }

            var afterDate = change.ScheduledDate ?? beforeDate;
            var afterObjective = change.Objective?.Trim() ?? beforeObjective;
            int changed;
            await using (var command = session.Connection.CreateCommand())
            {
                command.Transaction = session.Transaction;
                command.CommandText = """
                    update app.planned_sessions session
                    set scheduled_date = @scheduled_date, objective = @objective
                    from app.training_plan_versions version
                    where session.id = @target_session_id
                      and session.training_plan_version_id = version.id
                      and version.id = @target_version_id
                      and version.status = 'draft'
                      and @scheduled_date between version.period_start and version.period_end;
                    """;
                command.Parameters.AddWithValue("target_session_id", targetSessionId);
                command.Parameters.AddWithValue("target_version_id", targetVersionId);
                command.Parameters.AddWithValue("scheduled_date", afterDate);
                command.Parameters.AddWithValue("objective", afterObjective);
                changed = await command.ExecuteNonQueryAsync(cancellationToken);
            }

            if (changed == 0)
            {
                throw new PostgresException(
                    "Adjusted date must remain inside the plan period.",
                    PostgresErrorCodes.CheckViolation,
                    "", "");
            }

            var adjustmentType = change.ScheduledDate is not null && change.Objective is not null
                ? "reschedule_and_objective"
                : change.ScheduledDate is not null ? "reschedule" : "objective";
            var before = JsonSerializer.Serialize(new
            {
                plannedSessionId = change.SourcePlannedSessionId,
                scheduledDate = beforeDate,
                objective = beforeObjective
            });
            var after = JsonSerializer.Serialize(new
            {
                plannedSessionId = targetSessionId,
                scheduledDate = afterDate,
                objective = afterObjective
            });

            await using var adjustmentCommand = session.Connection.CreateCommand();
            adjustmentCommand.Transaction = session.Transaction;
            adjustmentCommand.CommandText = """
                insert into app.plan_adjustments (
                  owner_id, weekly_decision_id, source_plan_version_id,
                  target_plan_version_id, target_type, adjustment_type,
                  before_value, after_value, rationale, review_criterion)
                values (
                  @owner_id, @decision_id, @source_version_id,
                  @target_version_id, 'planned_session', @adjustment_type,
                  @before_value, @after_value, @rationale, @review_criterion);
                """;
            adjustmentCommand.Parameters.AddWithValue("owner_id", ownerId);
            adjustmentCommand.Parameters.AddWithValue("decision_id", decisionId);
            adjustmentCommand.Parameters.AddWithValue(
                "source_version_id", request.SourcePlanVersionId);
            adjustmentCommand.Parameters.AddWithValue("target_version_id", targetVersionId);
            adjustmentCommand.Parameters.AddWithValue("adjustment_type", adjustmentType);
            adjustmentCommand.Parameters.Add("before_value", NpgsqlDbType.Jsonb).Value = before;
            adjustmentCommand.Parameters.Add("after_value", NpgsqlDbType.Jsonb).Value = after;
            adjustmentCommand.Parameters.AddWithValue("rationale", request.Rationale.Trim());
            adjustmentCommand.Parameters.AddWithValue(
                "review_criterion", request.ReviewCriterion.Trim());
            await adjustmentCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditWriter.WriteAsync(
            session,
            ownerId,
            "training_plan.adjustment_draft_created",
            "training_plan_version",
            targetVersionId,
            correlationId,
            ["source_version_id", "session_changes", "status"],
            cancellationToken);
    }

    private static async Task<WeeklyEvaluationDetailResponse?> ReadDetailAsync(
        OwnerDbSession session,
        Guid evaluationId,
        CancellationToken cancellationToken)
    {
        WeeklyEvaluationSummaryResponse summary;
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select e.id, e.week_start, e.week_end, e.format_version,
                  e.plan_version_id, e.cutoff_at, e.status, e.traffic_light,
                  e.rationale, e.created_at, (d.id is not null)
                from app.weekly_evaluations e
                left join app.weekly_decisions d
                  on d.owner_id = e.owner_id and d.weekly_evaluation_id = e.id
                where e.id = @evaluation_id;
                """;
            command.Parameters.AddWithValue("evaluation_id", evaluationId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }
            summary = ReadSummary(reader);
        }

        var sources = new List<WeeklyEvaluationSessionResponse>();
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select source.id, source.planned_session_id, source.activity_id,
                  source.classification, source.execution_status,
                  session.scheduled_date, session.session_type, session.modality,
                  session.objective
                from app.weekly_evaluation_sessions source
                left join app.planned_sessions session
                  on session.owner_id = source.owner_id
                 and session.id = source.planned_session_id
                where source.weekly_evaluation_id = @evaluation_id
                order by session.scheduled_date, source.created_at, source.id;
                """;
            command.Parameters.AddWithValue("evaluation_id", evaluationId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                sources.Add(new WeeklyEvaluationSessionResponse(
                    reader.GetGuid(0),
                    Nullable<Guid>(reader, 1),
                    Nullable<Guid>(reader, 2),
                    reader.GetString(3),
                    NullableString(reader, 4),
                    Nullable<DateOnly>(reader, 5),
                    NullableString(reader, 6),
                    NullableString(reader, 7),
                    NullableString(reader, 8)));
            }
        }

        var metricOrder = new List<Guid>();
        var metrics = new Dictionary<Guid, MetricBuilder>();
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select id, metric_code, dimension, numeric_value,
                  boolean_value, text_value, unit, status, formula_version
                from app.weekly_metric_values
                where weekly_evaluation_id = @evaluation_id
                order by metric_code, dimension;
                """;
            command.Parameters.AddWithValue("evaluation_id", evaluationId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetGuid(0);
                metricOrder.Add(id);
                metrics.Add(id, new MetricBuilder(
                    id,
                    reader.GetString(1),
                    reader.GetString(2),
                    Nullable<decimal>(reader, 3),
                    Nullable<bool>(reader, 4),
                    NullableString(reader, 5),
                    NullableString(reader, 6),
                    reader.GetString(7),
                    reader.GetString(8)));
            }
        }

        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select evidence.weekly_metric_value_id,
                  case
                    when evidence.planned_session_id is not null then 'planned_session'
                    when evidence.activity_id is not null then 'activity'
                    when evidence.session_checkin_id is not null then 'session_checkin'
                    else 'observation'
                  end as source_type,
                  coalesce(evidence.planned_session_id, evidence.activity_id,
                    evidence.session_checkin_id, evidence.source_observation_id),
                  case
                    when evidence.planned_session_id is not null
                      then concat(session.scheduled_date, ' · ', session.session_type)
                    when evidence.activity_id is not null
                      then coalesce(activity.title, activity.activity_type)
                    when evidence.session_checkin_id is not null
                      then concat('Check-in ', checkin.checkin_window)
                    else 'Observación de origen'
                  end as label,
                  coalesce(
                    case when evidence.planned_session_id is not null
                      then '/plan?version=' || evaluation.plan_version_id::text
                        || '&session=' || evidence.planned_session_id::text end,
                    case when evidence.activity_id is not null and activity_link.planned_session_id is not null
                      then '/plan?version=' || evaluation.plan_version_id::text
                        || '&session=' || activity_link.planned_session_id::text
                        || '#completion' end,
                    case when evidence.session_checkin_id is not null and checkin.planned_session_id is not null
                      then '/plan?version=' || evaluation.plan_version_id::text
                        || '&session=' || checkin.planned_session_id::text
                        || '#completion' end,
                    '') as href
                from app.weekly_metric_evidence evidence
                join app.weekly_evaluations evaluation
                  on evaluation.owner_id = evidence.owner_id
                 and evaluation.id = @evaluation_id
                left join app.planned_sessions session
                  on session.owner_id = evidence.owner_id
                 and session.id = evidence.planned_session_id
                left join app.activities activity
                  on activity.owner_id = evidence.owner_id
                 and activity.id = evidence.activity_id
                left join app.activity_session_links activity_link
                  on activity_link.owner_id = evidence.owner_id
                 and activity_link.activity_id = evidence.activity_id
                 and activity_link.status = 'confirmed'
                left join app.session_checkins checkin
                  on checkin.owner_id = evidence.owner_id
                 and checkin.id = evidence.session_checkin_id
                where evidence.weekly_metric_value_id in (
                  select id from app.weekly_metric_values
                  where weekly_evaluation_id = @evaluation_id)
                order by evidence.created_at, evidence.id;
                """;
            command.Parameters.AddWithValue("evaluation_id", evaluationId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (metrics.TryGetValue(reader.GetGuid(0), out var metric))
                {
                    metric.Evidence.Add(new WeeklyMetricEvidenceResponse(
                        reader.GetString(1),
                        reader.GetGuid(2),
                        reader.GetString(3),
                        reader.GetString(4)));
                }
            }
        }

        WeeklyDecisionResponse? decision = null;
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select id, decision, observation, evidence, historical_comparison,
                  interpretation, recommendation, confirmed_by, confirmed_at
                from app.weekly_decisions
                where weekly_evaluation_id = @evaluation_id;
                """;
            command.Parameters.AddWithValue("evaluation_id", evaluationId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                decision = new WeeklyDecisionResponse(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetGuid(7),
                    reader.GetDateTime(8),
                    []);
            }
        }

        if (decision is not null)
        {
            var adjustments = new List<PlanAdjustmentResponse>();
            await using var command = session.Connection.CreateCommand();
            command.Transaction = session.Transaction;
            command.CommandText = """
                select id, source_plan_version_id, target_plan_version_id,
                  target_type, adjustment_type, before_value, after_value,
                  rationale, review_criterion, created_at
                from app.plan_adjustments
                where weekly_decision_id = @decision_id
                order by created_at, id;
                """;
            command.Parameters.AddWithValue("decision_id", decision.Id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                adjustments.Add(new PlanAdjustmentResponse(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    JsonDocument.Parse(reader.GetString(5)).RootElement.Clone(),
                    JsonDocument.Parse(reader.GetString(6)).RootElement.Clone(),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetDateTime(9)));
            }
            decision = decision with { Adjustments = adjustments };
        }

        return new WeeklyEvaluationDetailResponse(
            summary,
            sources,
            metricOrder.Select(id => metrics[id].Build()).ToArray(),
            decision);
    }

    private static WeeklyEvaluationSummaryResponse ReadSummary(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetFieldValue<DateOnly>(1),
        reader.GetFieldValue<DateOnly>(2),
        reader.GetString(3),
        Nullable<Guid>(reader, 4),
        reader.GetDateTime(5),
        reader.GetString(6),
        reader.GetString(7),
        reader.GetString(8),
        reader.GetDateTime(9),
        reader.GetBoolean(10));

    private static Dictionary<string, string[]> ValidateSnapshot(
        CreateWeeklyEvaluationSnapshotRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!WeeklyEvaluationRules.IsMonday(request.WeekStart))
        {
            errors[nameof(request.WeekStart)] = ["La semana debe comenzar en lunes."];
        }
        if (!WeeklyEvaluationRules.SnapshotStatuses.Contains(request.Status))
        {
            errors[nameof(request.Status)] = ["El estado debe ser provisional o final."];
        }
        return errors;
    }

    private static Dictionary<string, string[]> ValidateDecision(
        ConfirmWeeklyDecisionRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!WeeklyEvaluationRules.DecisionValues.Contains(request.Decision))
        {
            errors[nameof(request.Decision)] =
                ["La decisión debe ejecutar, adaptar, reducir o detener y valorar."];
        }
        Required(errors, nameof(request.Observation), request.Observation);
        Required(errors, nameof(request.Evidence), request.Evidence);
        Required(errors, nameof(request.HistoricalComparison), request.HistoricalComparison);
        Required(errors, nameof(request.Interpretation), request.Interpretation);
        Required(errors, nameof(request.Recommendation), request.Recommendation);

        if (WeeklyEvaluationRules.RequiresPlanAdjustment(request.Decision)
            && request.PlanAdjustment is null)
        {
            errors[nameof(request.PlanAdjustment)] =
                ["Adaptar o reducir requiere cambios exactos en una nueva versión del plan."];
        }
        if (!WeeklyEvaluationRules.RequiresPlanAdjustment(request.Decision)
            && request.PlanAdjustment is not null)
        {
            errors[nameof(request.PlanAdjustment)] =
                ["Solo las decisiones de adaptar o reducir pueden crear un ajuste."];
        }
        if (request.PlanAdjustment is not null)
        {
            if (request.PlanAdjustment.SourcePlanVersionId == Guid.Empty)
            {
                errors["planAdjustment.sourcePlanVersionId"] = ["Selecciona la versión de origen."];
            }
            Required(errors, "planAdjustment.rationale", request.PlanAdjustment.Rationale);
            Required(errors, "planAdjustment.reviewCriterion", request.PlanAdjustment.ReviewCriterion);
            if (request.PlanAdjustment.SessionChanges.Count == 0)
            {
                errors["planAdjustment.sessionChanges"] = ["Incluye al menos un cambio exacto."];
            }
            if (request.PlanAdjustment.SessionChanges.Count > 20)
            {
                errors["planAdjustment.sessionChanges"] = ["No se permiten más de 20 cambios por decisión."];
            }
            if (request.PlanAdjustment.SessionChanges
                .GroupBy(change => change.SourcePlannedSessionId)
                .Any(group => group.Count() > 1))
            {
                errors["planAdjustment.sessionChanges"] = ["Cada sesión puede cambiar una sola vez."];
            }
            foreach (var change in request.PlanAdjustment.SessionChanges)
            {
                if (change.SourcePlannedSessionId == Guid.Empty
                    || (change.ScheduledDate is null && string.IsNullOrWhiteSpace(change.Objective)))
                {
                    errors["planAdjustment.sessionChanges"] =
                        ["Cada cambio requiere una sesión y una fecha u objetivo nuevos."];
                    break;
                }
                if (change.Objective?.Trim().Length > 2000)
                {
                    errors["planAdjustment.sessionChanges"] =
                        ["El objetivo no puede exceder 2000 caracteres."];
                    break;
                }
            }
        }
        return errors;
    }

    private static void Required(
        IDictionary<string, string[]> errors,
        string name,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[name] = ["Este campo es obligatorio."];
        }
        else if (value.Trim().Length > 4000)
        {
            errors[name] = ["No puede exceder 4000 caracteres."];
        }
    }

    private static T? Nullable<T>(NpgsqlDataReader reader, int ordinal) where T : struct =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<T>(ordinal);

    private static string? NullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private sealed class MetricBuilder(
        Guid id,
        string metricCode,
        string dimension,
        decimal? numericValue,
        bool? booleanValue,
        string? textValue,
        string? unit,
        string status,
        string formulaVersion)
    {
        public List<WeeklyMetricEvidenceResponse> Evidence { get; } = [];

        public WeeklyMetricValueResponse Build() => new(
            id,
            metricCode,
            dimension,
            numericValue,
            booleanValue,
            textValue,
            unit,
            status,
            formulaVersion,
            Evidence);
    }

    private sealed class PlanAdjustmentNotFoundException(string message)
        : Exception(message);
}

public sealed record CreateWeeklyEvaluationSnapshotRequest(DateOnly WeekStart, string Status);

public sealed record ConfirmWeeklyDecisionRequest(
    string Decision,
    string Observation,
    string Evidence,
    string HistoricalComparison,
    string Interpretation,
    string Recommendation,
    PlanVersionAdjustmentRequest? PlanAdjustment);

public sealed record PlanVersionAdjustmentRequest(
    Guid SourcePlanVersionId,
    string Rationale,
    string ReviewCriterion,
    IReadOnlyList<PlannedSessionAdjustmentRequest> SessionChanges);

public sealed record PlannedSessionAdjustmentRequest(
    Guid SourcePlannedSessionId,
    DateOnly? ScheduledDate,
    string? Objective);

public sealed record WeeklyEvaluationSummaryResponse(
    Guid Id,
    DateOnly WeekStart,
    DateOnly WeekEnd,
    string FormatVersion,
    Guid? PlanVersionId,
    DateTime CutoffAt,
    string Status,
    string TrafficLight,
    string Rationale,
    DateTime CreatedAt,
    bool HasDecision);

public sealed record WeeklyEvaluationSessionResponse(
    Guid Id,
    Guid? PlannedSessionId,
    Guid? ActivityId,
    string Classification,
    string? ExecutionStatus,
    DateOnly? ScheduledDate,
    string? SessionType,
    string? Modality,
    string? Objective);

public sealed record WeeklyMetricEvidenceResponse(
    string SourceType,
    Guid SourceId,
    string Label,
    string Href);

public sealed record WeeklyMetricValueResponse(
    Guid Id,
    string MetricCode,
    string Dimension,
    decimal? NumericValue,
    bool? BooleanValue,
    string? TextValue,
    string? Unit,
    string Status,
    string FormulaVersion,
    IReadOnlyList<WeeklyMetricEvidenceResponse> Evidence);

public sealed record PlanAdjustmentResponse(
    Guid Id,
    Guid SourcePlanVersionId,
    Guid TargetPlanVersionId,
    string TargetType,
    string AdjustmentType,
    JsonElement BeforeValue,
    JsonElement AfterValue,
    string Rationale,
    string ReviewCriterion,
    DateTime CreatedAt);

public sealed record WeeklyDecisionResponse(
    Guid Id,
    string Decision,
    string Observation,
    string Evidence,
    string HistoricalComparison,
    string Interpretation,
    string Recommendation,
    Guid ConfirmedBy,
    DateTime ConfirmedAt,
    IReadOnlyList<PlanAdjustmentResponse> Adjustments);

public sealed record WeeklyEvaluationDetailResponse(
    WeeklyEvaluationSummaryResponse Evaluation,
    IReadOnlyList<WeeklyEvaluationSessionResponse> Sessions,
    IReadOnlyList<WeeklyMetricValueResponse> Metrics,
    WeeklyDecisionResponse? Decision);
