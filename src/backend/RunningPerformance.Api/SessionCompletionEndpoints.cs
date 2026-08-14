using System.Security.Claims;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using RunningPerformance.Api.Authentication;
using RunningPerformance.Api.Http;
using RunningPerformance.Infrastructure.Database;

namespace RunningPerformance.Api.Features;

public static class SessionCompletionEndpoints
{
    private static readonly HashSet<string> ExecutionStatuses =
    [
        "completed_as_planned",
        "completed_modified",
        "valid_substitution",
        "not_completed",
        "optional_not_completed"
    ];

    private static readonly HashSet<string> LinkStatusChanges =
        ["confirmed", "withdrawn", "rejected"];

    private static readonly HashSet<string> CheckinWindows =
        ["immediate", "24h", "48h"];

    private static readonly HashSet<string> RecoveryResponses =
        ["normal", "incomplete", "adverse"];

    public static IEndpointRouteBuilder MapSessionCompletionEndpoints(
        this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/sessions").WithTags("Sessions");

        group.MapGet("/{sessionId:guid}/completion", GetAsync)
            .WithName("GetSessionCompletion")
            .Produces<SessionCompletionResponse>()
            .Produces(StatusCodes.Status404NotFound);
        group.MapPost("/{sessionId:guid}/links/proposals", ProposeAutomaticLinkAsync)
            .WithName("CreateAutomaticSessionLinkProposal")
            .Produces<SessionCompletionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        group.MapPost("/{sessionId:guid}/links", LinkActivityAsync)
            .WithName("LinkSessionActivity")
            .Produces<SessionCompletionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        group.MapPut("/{sessionId:guid}/links/{linkId:guid}", ChangeLinkStatusAsync)
            .WithName("ChangeSessionActivityLink")
            .Produces<SessionCompletionResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        group.MapPut("/{sessionId:guid}/outcome", SaveOutcomeAsync)
            .WithName("SavePlannedSessionOutcome")
            .Produces<SessionCompletionResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound);
        group.MapPut("/{sessionId:guid}/checkins/{checkinWindow}", SaveCheckinAsync)
            .WithName("SaveSessionCheckin")
            .Produces<SessionCompletionResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> GetAsync(
        Guid sessionId,
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var response = await ReadAsync(session, sessionId, cancellationToken);
        await session.CommitAsync(cancellationToken);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }

    private static async Task<IResult> ProposeAutomaticLinkAsync(
        Guid sessionId,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var target = await ReadSessionTargetAsync(session, sessionId, cancellationToken);
        if (target is null)
        {
            return Results.NotFound();
        }

        if (target.VersionStatus == "draft")
        {
            return DraftConflict();
        }

        var candidateIds = new List<Guid>();
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select activity.id
                from app.activities activity
                where activity.started_at_local::date = @scheduled_date
                  and activity.validation_status <> 'quarantined'
                  and (
                    (@running and activity.activity_category = 'running')
                    or (@strength and activity.activity_category = 'strength')
                    or (not @running and not @strength))
                  and not exists (
                    select 1
                    from app.activity_session_links active_link
                    where active_link.activity_id = activity.id
                      and active_link.status in ('proposed', 'confirmed'))
                order by activity.started_at_local, activity.id;
                """;
            command.Parameters.AddWithValue("scheduled_date", target.ScheduledDate);
            command.Parameters.AddWithValue("running", IsRunning(target));
            command.Parameters.AddWithValue("strength", IsStrength(target));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                candidateIds.Add(reader.GetGuid(0));
            }
        }

        if (candidateIds.Count != 1)
        {
            return Results.Problem(
                title: "No hay una coincidencia automática única",
                detail: candidateIds.Count == 0
                    ? "No se encontró una actividad compatible, sin vínculo y en la fecha planificada."
                    : "Hay varias actividades compatibles. Selecciónalas manualmente para conservar una sesión lógica sin adivinar.",
                statusCode: StatusCodes.Status409Conflict);
        }

        await InsertLinkAsync(
            session,
            ownerId,
            sessionId,
            candidateIds[0],
            "automatic",
            "proposed",
            JsonSerializer.Serialize(new
            {
                ruleVersion = "APP-010-v1",
                exactLocalDate = true,
                compatibleActivityCategory = true,
                uniqueCandidate = true
            }),
            0.900m,
            null,
            cancellationToken);
        await AuditWriter.WriteAsync(
            session,
            ownerId,
            "activity_session_link.proposed",
            "planned_session",
            sessionId,
            httpContext.GetCorrelationId(),
            ["activity_id", "method", "criteria", "confidence", "status"],
            cancellationToken);

        var response = await ReadAsync(session, sessionId, cancellationToken)
            ?? throw new InvalidOperationException("Session disappeared after link proposal.");
        await session.CommitAsync(cancellationToken);
        return Results.Created($"/api/v1/sessions/{sessionId}/completion", response);
    }

    private static async Task<IResult> LinkActivityAsync(
        Guid sessionId,
        LinkSessionActivityRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        if (request.ActivityId == Guid.Empty)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.ActivityId)] = ["Selecciona una actividad."]
            });
        }

        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var target = await ReadSessionTargetAsync(session, sessionId, cancellationToken);
        if (target is null)
        {
            return Results.NotFound();
        }

        if (target.VersionStatus == "draft")
        {
            return DraftConflict();
        }

        if (!await ActivityExistsAsync(session, request.ActivityId, cancellationToken))
        {
            return Results.NotFound();
        }

        ActiveLink? previous = null;
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select id, planned_session_id, status
                from app.activity_session_links
                where activity_id = @activity_id
                  and status in ('proposed', 'confirmed')
                for update;
                """;
            command.Parameters.AddWithValue("activity_id", request.ActivityId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                previous = new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2));
            }
        }

        if (previous is { PlannedSessionId: var existingSession, Status: "confirmed" }
            && existingSession == sessionId)
        {
            var unchanged = await ReadAsync(session, sessionId, cancellationToken)
                ?? throw new InvalidOperationException("Session disappeared while reading its link.");
            await session.CommitAsync(cancellationToken);
            return Results.Ok(unchanged);
        }

        if (previous is not null)
        {
            await ChangeStoredLinkStatusAsync(
                session,
                previous.Id,
                "withdrawn",
                cancellationToken);
        }

        await InsertLinkAsync(
            session,
            ownerId,
            sessionId,
            request.ActivityId,
            "manual",
            "confirmed",
            "{\"source\":\"athlete_selection\"}",
            null,
            previous?.Id,
            cancellationToken);
        await AuditWriter.WriteAsync(
            session,
            ownerId,
            previous is null
                ? "activity_session_link.confirmed"
                : "activity_session_link.changed",
            "planned_session",
            sessionId,
            httpContext.GetCorrelationId(),
            ["activity_id", "planned_session_id", "status", "supersedes_id"],
            cancellationToken);

        var response = await ReadAsync(session, sessionId, cancellationToken)
            ?? throw new InvalidOperationException("Session disappeared after activity link.");
        await session.CommitAsync(cancellationToken);
        return Results.Created($"/api/v1/sessions/{sessionId}/completion", response);
    }

    private static async Task<IResult> ChangeLinkStatusAsync(
        Guid sessionId,
        Guid linkId,
        ChangeSessionActivityLinkRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        if (!LinkStatusChanges.Contains(request.Status))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Status)] = ["El estado debe ser confirmed, withdrawn o rejected."]
            });
        }

        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var target = await ReadSessionTargetAsync(session, sessionId, cancellationToken);
        if (target is null)
        {
            return Results.NotFound();
        }

        if (target.VersionStatus == "draft")
        {
            return DraftConflict();
        }

        string? currentStatus;
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select status
                from app.activity_session_links
                where id = @link_id and planned_session_id = @session_id
                for update;
                """;
            command.Parameters.AddWithValue("link_id", linkId);
            command.Parameters.AddWithValue("session_id", sessionId);
            currentStatus = (string?)await command.ExecuteScalarAsync(cancellationToken);
        }

        if (currentStatus is null)
        {
            return Results.NotFound();
        }

        var validTransition = (currentStatus, request.Status) switch
        {
            ("proposed", "confirmed" or "withdrawn" or "rejected") => true,
            ("confirmed", "withdrawn") => true,
            _ when currentStatus == request.Status => true,
            _ => false
        };
        if (!validTransition)
        {
            return Results.Problem(
                title: "Transición de vínculo no válida",
                detail: $"Un vínculo {currentStatus} no puede cambiar a {request.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (currentStatus != request.Status)
        {
            await ChangeStoredLinkStatusAsync(session, linkId, request.Status, cancellationToken);
            await AuditWriter.WriteAsync(
                session,
                ownerId,
                $"activity_session_link.{request.Status}",
                "activity_session_link",
                linkId,
                httpContext.GetCorrelationId(),
                ["status"],
                cancellationToken);
        }

        var response = await ReadAsync(session, sessionId, cancellationToken)
            ?? throw new InvalidOperationException("Session disappeared after link update.");
        await session.CommitAsync(cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> SaveOutcomeAsync(
        Guid sessionId,
        SavePlannedSessionOutcomeRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var errors = ValidateOutcome(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var target = await ReadSessionTargetAsync(session, sessionId, cancellationToken);
        if (target is null)
        {
            return Results.NotFound();
        }

        if (target.VersionStatus == "draft")
        {
            return DraftConflict();
        }

        if (request.ExecutionStatus == "optional_not_completed"
            && target.Obligation != "optional")
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.ExecutionStatus)] =
                    ["Solo una sesión opcional puede marcarse como opcional no realizada."]
            });
        }

        Guid outcomeId;
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                insert into app.planned_session_outcomes (
                  owner_id, planned_session_id, execution_status,
                  modification_reason, confirmed_at)
                values (
                  @owner_id, @session_id, @execution_status,
                  @modification_reason, now())
                on conflict (planned_session_id) do update
                set execution_status = excluded.execution_status,
                    modification_reason = excluded.modification_reason,
                    confirmed_at = excluded.confirmed_at,
                    updated_at = now()
                returning id;
                """;
            command.Parameters.AddWithValue("owner_id", ownerId);
            command.Parameters.AddWithValue("session_id", sessionId);
            command.Parameters.AddWithValue("execution_status", request.ExecutionStatus);
            AddNullableText(command, "modification_reason", request.Reason);
            outcomeId = (Guid)(await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Outcome upsert did not return an ID."));
        }

        await AuditWriter.WriteAsync(
            session,
            ownerId,
            "planned_session_outcome.confirmed",
            "planned_session_outcome",
            outcomeId,
            httpContext.GetCorrelationId(),
            ["execution_status", "modification_reason", "confirmed_at"],
            cancellationToken);

        var response = await ReadAsync(session, sessionId, cancellationToken)
            ?? throw new InvalidOperationException("Session disappeared after outcome save.");
        await session.CommitAsync(cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> SaveCheckinAsync(
        Guid sessionId,
        string checkinWindow,
        SaveSessionCheckinRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        OwnerDataSource dataSource,
        CancellationToken cancellationToken)
    {
        checkinWindow = checkinWindow.ToLowerInvariant();
        var errors = ValidateCheckin(checkinWindow, request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var target = await ReadSessionTargetAsync(session, sessionId, cancellationToken);
        if (target is null)
        {
            return Results.NotFound();
        }

        if (target.VersionStatus == "draft")
        {
            return DraftConflict();
        }

        Guid checkinId;
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                insert into app.session_checkins (
                  owner_id, planned_session_id, checkin_window, session_rpe,
                  pain, pain_location, gait_changed, fatigue, sleep_quality,
                  perceived_recovery, has_illness_or_symptom, symptom_note,
                  recovery_response, note, recorded_at)
                values (
                  @owner_id, @session_id, @checkin_window, @session_rpe,
                  @pain, @pain_location, @gait_changed, @fatigue, @sleep_quality,
                  @perceived_recovery, @has_illness_or_symptom, @symptom_note,
                  @recovery_response, @note, now())
                on conflict (planned_session_id, checkin_window)
                  where planned_session_id is not null
                do update set
                  session_rpe = excluded.session_rpe,
                  pain = excluded.pain,
                  pain_location = excluded.pain_location,
                  gait_changed = excluded.gait_changed,
                  fatigue = excluded.fatigue,
                  sleep_quality = excluded.sleep_quality,
                  perceived_recovery = excluded.perceived_recovery,
                  has_illness_or_symptom = excluded.has_illness_or_symptom,
                  symptom_note = excluded.symptom_note,
                  recovery_response = excluded.recovery_response,
                  note = excluded.note,
                  recorded_at = excluded.recorded_at,
                  updated_at = now()
                returning id;
                """;
            command.Parameters.AddWithValue("owner_id", ownerId);
            command.Parameters.AddWithValue("session_id", sessionId);
            command.Parameters.AddWithValue("checkin_window", checkinWindow);
            AddNullable(command, "session_rpe", NpgsqlDbType.Numeric, request.SessionRpe);
            AddNullable(command, "pain", NpgsqlDbType.Numeric, request.Pain);
            AddNullableText(command, "pain_location", request.PainLocation);
            AddNullable(command, "gait_changed", NpgsqlDbType.Boolean, request.GaitChanged);
            AddNullable(command, "fatigue", NpgsqlDbType.Numeric, request.Fatigue);
            AddNullable(command, "sleep_quality", NpgsqlDbType.Numeric, request.SleepQuality);
            AddNullable(command, "perceived_recovery", NpgsqlDbType.Numeric, request.PerceivedRecovery);
            AddNullable(
                command,
                "has_illness_or_symptom",
                NpgsqlDbType.Boolean,
                request.HasIllnessOrSymptom);
            AddNullableText(command, "symptom_note", request.SymptomNote);
            AddNullableText(command, "recovery_response", request.RecoveryResponse);
            AddNullableText(command, "note", request.Note);
            checkinId = (Guid)(await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Check-in upsert did not return an ID."));
        }

        await AuditWriter.WriteAsync(
            session,
            ownerId,
            "session_checkin.saved",
            "session_checkin",
            checkinId,
            httpContext.GetCorrelationId(),
            [
                "checkin_window", "session_rpe", "pain", "pain_location",
                "gait_changed", "fatigue", "sleep_quality", "perceived_recovery",
                "has_illness_or_symptom", "symptom_note", "recovery_response", "note"
            ],
            cancellationToken);

        var response = await ReadAsync(session, sessionId, cancellationToken)
            ?? throw new InvalidOperationException("Session disappeared after check-in save.");
        await session.CommitAsync(cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<SessionCompletionResponse?> ReadAsync(
        OwnerDbSession session,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var target = await ReadSessionTargetAsync(session, sessionId, cancellationToken);
        if (target is null)
        {
            return null;
        }

        PlannedSessionOutcomeResponse? outcome = null;
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select id, execution_status, modification_reason, confirmed_at, updated_at
                from app.planned_session_outcomes
                where planned_session_id = @session_id;
                """;
            command.Parameters.AddWithValue("session_id", sessionId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                outcome = new(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    NullableString(reader, 2),
                    Nullable<DateTime>(reader, 3),
                    reader.GetDateTime(4));
            }
        }

        var links = new List<SessionActivityLinkResponse>();
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select
                  link.id, link.method, link.criteria::text, link.confidence,
                  link.status, link.supersedes_id, link.created_at, link.updated_at,
                  activity.id, activity.garmin_activity_id, activity.title,
                  activity.activity_type, activity.activity_category, activity.modality,
                  activity.started_at_local, activity.distance_m, activity.duration_seconds
                from app.activity_session_links link
                join app.activities activity
                  on activity.owner_id = link.owner_id
                 and activity.id = link.activity_id
                where link.planned_session_id = @session_id
                order by
                  case when link.status in ('proposed', 'confirmed') then 0 else 1 end,
                  activity.started_at_local,
                  link.created_at;
                """;
            command.Parameters.AddWithValue("session_id", sessionId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                using var criteria = JsonDocument.Parse(reader.GetString(2));
                links.Add(new(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    criteria.RootElement.Clone(),
                    Nullable<decimal>(reader, 3),
                    reader.GetString(4),
                    Nullable<Guid>(reader, 5),
                    reader.GetDateTime(6),
                    reader.GetDateTime(7),
                    ReadActivity(reader, 8)));
            }
        }

        var checkins = new List<SessionCheckinResponse>();
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select
                  id, checkin_window, session_rpe, pain, pain_location,
                  gait_changed, fatigue, sleep_quality, perceived_recovery,
                  has_illness_or_symptom, symptom_note, recovery_response,
                  note, recorded_at, updated_at
                from app.session_checkins
                where planned_session_id = @session_id
                order by case checkin_window when 'immediate' then 0 when '24h' then 1 else 2 end;
                """;
            command.Parameters.AddWithValue("session_id", sessionId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                checkins.Add(new(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    Nullable<decimal>(reader, 2),
                    Nullable<decimal>(reader, 3),
                    NullableString(reader, 4),
                    Nullable<bool>(reader, 5),
                    Nullable<decimal>(reader, 6),
                    Nullable<decimal>(reader, 7),
                    Nullable<decimal>(reader, 8),
                    Nullable<bool>(reader, 9),
                    NullableString(reader, 10),
                    NullableString(reader, 11),
                    NullableString(reader, 12),
                    reader.GetDateTime(13),
                    reader.GetDateTime(14)));
            }
        }

        var candidates = new List<SessionActivityCandidateResponse>();
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select
                  activity.id, activity.garmin_activity_id, activity.title,
                  activity.activity_type, activity.activity_category, activity.modality,
                  activity.started_at_local, activity.distance_m, activity.duration_seconds,
                  active_link.id, active_link.planned_session_id, active_link.status,
                  (activity.started_at_local::date = @scheduled_date
                    and (
                      (@running and activity.activity_category = 'running')
                      or (@strength and activity.activity_category = 'strength')
                      or (not @running and not @strength))) as is_exact_match
                from app.activities activity
                left join app.activity_session_links active_link
                  on active_link.owner_id = activity.owner_id
                 and active_link.activity_id = activity.id
                 and active_link.status in ('proposed', 'confirmed')
                where activity.started_at_local::date
                  between @scheduled_date - 2 and @scheduled_date + 2
                  and activity.validation_status <> 'quarantined'
                order by is_exact_match desc, activity.started_at_local, activity.id
                limit 50;
                """;
            command.Parameters.AddWithValue("scheduled_date", target.ScheduledDate);
            command.Parameters.AddWithValue("running", IsRunning(target));
            command.Parameters.AddWithValue("strength", IsStrength(target));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add(new(
                    ReadActivity(reader, 0),
                    Nullable<Guid>(reader, 9),
                    Nullable<Guid>(reader, 10),
                    NullableString(reader, 11),
                    reader.GetBoolean(12)));
            }
        }

        SessionLogicalLoadResponse load;
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select activity_count, distance_m, duration_seconds, session_rpe, srpe_load
                from app.v_logical_session_srpe
                where planned_session_id = @session_id;
                """;
            command.Parameters.AddWithValue("session_id", sessionId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Logical session load view returned no row.");
            }

            load = new(
                reader.GetInt32(0),
                Nullable<decimal>(reader, 1),
                Nullable<decimal>(reader, 2),
                Nullable<decimal>(reader, 3),
                Nullable<decimal>(reader, 4));
        }

        return new(
            target.Id,
            target.ScheduledDate,
            target.SessionType,
            target.Modality,
            target.Obligation,
            target.Objective,
            target.VersionStatus,
            outcome,
            links,
            candidates,
            checkins,
            load);
    }

    private static async Task<SessionTarget?> ReadSessionTargetAsync(
        OwnerDbSession session,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            select
              planned.id, planned.scheduled_date, planned.session_type,
              planned.modality, planned.obligation, planned.objective,
              version.status
            from app.planned_sessions planned
            join app.training_plan_versions version
              on version.owner_id = planned.owner_id
             and version.id = planned.training_plan_version_id
            where planned.id = @session_id;
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(
                reader.GetGuid(0),
                reader.GetFieldValue<DateOnly>(1),
                reader.GetString(2),
                NullableString(reader, 3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6))
            : null;
    }

    private static async Task<bool> ActivityExistsAsync(
        OwnerDbSession session,
        Guid activityId,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = "select exists(select 1 from app.activities where id = @activity_id);";
        command.Parameters.AddWithValue("activity_id", activityId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task InsertLinkAsync(
        OwnerDbSession session,
        Guid ownerId,
        Guid sessionId,
        Guid activityId,
        string method,
        string status,
        string criteria,
        decimal? confidence,
        Guid? supersedesId,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            insert into app.activity_session_links (
              owner_id, activity_id, planned_session_id, method, criteria,
              confidence, status, supersedes_id, actor_id)
            values (
              @owner_id, @activity_id, @session_id, @method, @criteria,
              @confidence, @status, @supersedes_id, @actor_id);
            """;
        command.Parameters.AddWithValue("owner_id", ownerId);
        command.Parameters.AddWithValue("activity_id", activityId);
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("method", method);
        command.Parameters.Add("criteria", NpgsqlDbType.Jsonb).Value = criteria;
        AddNullable(command, "confidence", NpgsqlDbType.Numeric, confidence);
        AddNullable(command, "supersedes_id", NpgsqlDbType.Uuid, supersedesId);
        AddNullable(
            command,
            "actor_id",
            NpgsqlDbType.Uuid,
            method == "manual" ? ownerId : (Guid?)null);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ChangeStoredLinkStatusAsync(
        OwnerDbSession session,
        Guid linkId,
        string status,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            update app.activity_session_links
            set status = @status
            where id = @link_id;
            """;
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("link_id", linkId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Dictionary<string, string[]> ValidateOutcome(
        SavePlannedSessionOutcomeRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!ExecutionStatuses.Contains(request.ExecutionStatus))
        {
            errors[nameof(request.ExecutionStatus)] = ["Selecciona uno de los cinco estados TRN-003."];
        }

        var reasonRequired = request.ExecutionStatus is
            "completed_modified" or "valid_substitution" or "not_completed";
        if (reasonRequired && string.IsNullOrWhiteSpace(request.Reason))
        {
            errors[nameof(request.Reason)] = ["Explica la modificación, sustitución u omisión."];
        }
        else if (request.Reason?.Trim().Length > 2000)
        {
            errors[nameof(request.Reason)] = ["No puede exceder 2000 caracteres."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateCheckin(
        string checkinWindow,
        SaveSessionCheckinRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!CheckinWindows.Contains(checkinWindow))
        {
            errors["checkinWindow"] = ["La ventana debe ser immediate, 24h o 48h."];
        }

        Range(errors, nameof(request.SessionRpe), request.SessionRpe, 1, 10);
        Range(errors, nameof(request.Pain), request.Pain, 0, 10);
        Range(errors, nameof(request.Fatigue), request.Fatigue, 0, 10);
        Range(errors, nameof(request.SleepQuality), request.SleepQuality, 1, 5);
        Range(errors, nameof(request.PerceivedRecovery), request.PerceivedRecovery, 0, 10);
        Length(errors, nameof(request.PainLocation), request.PainLocation, 120);
        Length(errors, nameof(request.SymptomNote), request.SymptomNote, 500);
        Length(errors, nameof(request.Note), request.Note, 2000);

        if (checkinWindow != "immediate" && request.SessionRpe is not null)
        {
            errors[nameof(request.SessionRpe)] = ["El RPE global se registra en el check-in inmediato."];
        }

        if (checkinWindow == "immediate" && request.RecoveryResponse is not null)
        {
            errors[nameof(request.RecoveryResponse)] =
                ["La respuesta posterior corresponde a las ventanas de 24 h o 48 h."];
        }
        else if (request.RecoveryResponse is not null
            && !RecoveryResponses.Contains(request.RecoveryResponse))
        {
            errors[nameof(request.RecoveryResponse)] =
                ["La respuesta debe ser normal, incomplete o adverse."];
        }

        if (request is
            {
                SessionRpe: null,
                Pain: null,
                PainLocation: null,
                GaitChanged: null,
                Fatigue: null,
                SleepQuality: null,
                PerceivedRecovery: null,
                HasIllnessOrSymptom: null,
                SymptomNote: null,
                RecoveryResponse: null,
                Note: null
            })
        {
            errors["checkin"] = ["Captura al menos un dato; lo desconocido permanece vacío."];
        }

        return errors;
    }

    private static void Range(
        IDictionary<string, string[]> errors,
        string name,
        decimal? value,
        decimal minimum,
        decimal maximum)
    {
        if (value is not null && (value < minimum || value > maximum))
        {
            errors[name] = [$"Debe estar entre {minimum} y {maximum}."];
        }
    }

    private static void Length(
        IDictionary<string, string[]> errors,
        string name,
        string? value,
        int maximum)
    {
        if (value?.Trim().Length > maximum)
        {
            errors[name] = [$"No puede exceder {maximum} caracteres."];
        }
    }

    private static bool IsRunning(SessionTarget target) =>
        target.Modality == "running"
        || target.SessionType.Contains("run", StringComparison.OrdinalIgnoreCase);

    private static bool IsStrength(SessionTarget target) =>
        target.SessionType.Contains("strength", StringComparison.OrdinalIgnoreCase);

    private static SessionActivityResponse ReadActivity(NpgsqlDataReader reader, int offset) => new(
        reader.GetGuid(offset),
        Nullable<long>(reader, offset + 1),
        NullableString(reader, offset + 2),
        reader.GetString(offset + 3),
        NullableString(reader, offset + 4),
        NullableString(reader, offset + 5),
        reader.GetDateTime(offset + 6),
        Nullable<decimal>(reader, offset + 7),
        Nullable<decimal>(reader, offset + 8));

    private static T? Nullable<T>(NpgsqlDataReader reader, int ordinal)
        where T : struct =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<T>(ordinal);

    private static string? NullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static void AddNullable<T>(
        NpgsqlCommand command,
        string name,
        NpgsqlDbType type,
        T? value)
        where T : struct =>
        command.Parameters.Add(name, type).Value = value.HasValue ? value.Value : DBNull.Value;

    private static void AddNullableText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.Add(name, NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static IResult DraftConflict() => Results.Problem(
        title: "El borrador todavía no se ejecuta",
        detail: "Publica la versión antes de registrar actividades, resultado o recuperación.",
        statusCode: StatusCodes.Status409Conflict);

    private sealed record SessionTarget(
        Guid Id,
        DateOnly ScheduledDate,
        string SessionType,
        string? Modality,
        string Obligation,
        string Objective,
        string VersionStatus);

    private sealed record ActiveLink(Guid Id, Guid PlannedSessionId, string Status);
}

public sealed record LinkSessionActivityRequest(Guid ActivityId);

public sealed record ChangeSessionActivityLinkRequest(string Status);

public sealed record SavePlannedSessionOutcomeRequest(string ExecutionStatus, string? Reason);

public sealed record SaveSessionCheckinRequest(
    decimal? SessionRpe,
    decimal? Pain,
    string? PainLocation,
    bool? GaitChanged,
    decimal? Fatigue,
    decimal? SleepQuality,
    decimal? PerceivedRecovery,
    bool? HasIllnessOrSymptom,
    string? SymptomNote,
    string? RecoveryResponse,
    string? Note);

public sealed record SessionActivityResponse(
    Guid Id,
    long? GarminActivityId,
    string? Title,
    string ActivityType,
    string? ActivityCategory,
    string? Modality,
    DateTime StartedAtLocal,
    decimal? DistanceM,
    decimal? DurationSeconds);

public sealed record SessionActivityLinkResponse(
    Guid Id,
    string Method,
    JsonElement Criteria,
    decimal? Confidence,
    string Status,
    Guid? SupersedesId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    SessionActivityResponse Activity);

public sealed record SessionActivityCandidateResponse(
    SessionActivityResponse Activity,
    Guid? ActiveLinkId,
    Guid? ActivePlannedSessionId,
    string? ActiveLinkStatus,
    bool IsExactMatch);

public sealed record PlannedSessionOutcomeResponse(
    Guid Id,
    string ExecutionStatus,
    string? Reason,
    DateTime? ConfirmedAt,
    DateTime UpdatedAt);

public sealed record SessionCheckinResponse(
    Guid Id,
    string CheckinWindow,
    decimal? SessionRpe,
    decimal? Pain,
    string? PainLocation,
    bool? GaitChanged,
    decimal? Fatigue,
    decimal? SleepQuality,
    decimal? PerceivedRecovery,
    bool? HasIllnessOrSymptom,
    string? SymptomNote,
    string? RecoveryResponse,
    string? Note,
    DateTime RecordedAt,
    DateTime UpdatedAt);

public sealed record SessionLogicalLoadResponse(
    int ActivityCount,
    decimal? DistanceM,
    decimal? DurationSeconds,
    decimal? SessionRpe,
    decimal? SrpeLoad);

public sealed record SessionCompletionResponse(
    Guid PlannedSessionId,
    DateOnly ScheduledDate,
    string SessionType,
    string? Modality,
    string Obligation,
    string Objective,
    string PlanVersionStatus,
    PlannedSessionOutcomeResponse? Outcome,
    IReadOnlyList<SessionActivityLinkResponse> Links,
    IReadOnlyList<SessionActivityCandidateResponse> Candidates,
    IReadOnlyList<SessionCheckinResponse> Checkins,
    SessionLogicalLoadResponse Load);
