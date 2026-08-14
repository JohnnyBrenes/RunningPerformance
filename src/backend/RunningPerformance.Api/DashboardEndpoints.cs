using System.Security.Claims;
using Npgsql;
using RunningPerformance.Api.Authentication;
using RunningPerformance.Application.Dashboard;
using RunningPerformance.Application.FreeTier;
using RunningPerformance.Infrastructure.Database;

namespace RunningPerformance.Api.Features;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/dashboard", GetAsync)
            .WithName("GetDashboard")
            .WithTags("Dashboard")
            .Produces<DashboardResponse>()
            .ProducesValidationProblem();
        return routes;
    }

    private static async Task<IResult> GetAsync(
        int weeks,
        ClaimsPrincipal principal,
        OwnerDataSource dataSource,
        FreeTierQuotaGuard quotaGuard,
        FreeTierQuotaOptions quotaOptions,
        CancellationToken cancellationToken)
    {
        if (!DashboardRules.IsSupportedWindow(weeks))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(weeks)] = ["La ventana debe ser de 4, 8 o 12 semanas."]
            });
        }

        var ownerId = principal.GetRequiredOwnerId();
        await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
        var asOf = await ReadCurrentDateAsync(session, cancellationToken);
        var nextSession = await ReadNextSessionAsync(session, cancellationToken);
        var currentWeek = await ReadCurrentWeekAsync(session, cancellationToken);
        var latestRecovery = await ReadLatestRecoveryAsync(session, cancellationToken);
        var weeksByDate = await ReadTrendWeeksAsync(session, weeks, cancellationToken);
        await ReadModalitiesAsync(session, weeksByDate, cancellationToken);
        await ReadSourcesAsync(session, weeksByDate, cancellationToken);
        var pillars = await ReadPillarsAsync(session, cancellationToken);
        var alerts = await ReadPendingAlertsAsync(session, cancellationToken);
        var quota = await ReadQuotaAsync(
            session,
            quotaGuard,
            quotaOptions,
            alerts,
            cancellationToken);
        await session.CommitAsync(cancellationToken);

        return Results.Ok(new DashboardResponse(
            asOf,
            weeks,
            nextSession,
            currentWeek,
            latestRecovery,
            weeksByDate.Values.OrderBy(item => item.WeekStart).Select(item => item.ToResponse()).ToArray(),
            pillars,
            alerts,
            quota));
    }

    private static async Task<DateOnly> ReadCurrentDateAsync(
        OwnerDbSession session,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = "select current_date;";
        return (DateOnly)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Database date is unavailable."));
    }

    private static async Task<DashboardNextSessionResponse?> ReadNextSessionAsync(
        OwnerDbSession session,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            select session.id, session.training_plan_version_id,
              version.version_number, session.scheduled_date, session.session_type,
              session.modality, session.obligation, session.objective,
              session.distance_m, session.duration_seconds,
              session.target_rpe_min, session.target_rpe_max
            from app.planned_sessions session
            join app.training_plan_versions version
              on version.owner_id = session.owner_id
             and version.id = session.training_plan_version_id
            where version.status = 'published'
              and session.scheduled_date >= current_date
            order by session.scheduled_date, session.id
            limit 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var sessionId = reader.GetGuid(0);
        var versionId = reader.GetGuid(1);
        return new DashboardNextSessionResponse(
            sessionId,
            versionId,
            reader.GetInt32(2),
            reader.GetFieldValue<DateOnly>(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetDecimal(8),
            reader.IsDBNull(9) ? null : reader.GetDecimal(9),
            reader.IsDBNull(10) ? null : reader.GetDecimal(10),
            reader.IsDBNull(11) ? null : reader.GetDecimal(11),
            $"/plan?version={versionId}&session={sessionId}");
    }

    private static async Task<DashboardCurrentWeekResponse> ReadCurrentWeekAsync(
        OwnerDbSession session,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            with current_sessions as (
              select session.id
              from app.planned_sessions session
              join app.training_plan_versions version
                on version.owner_id = session.owner_id
               and version.id = session.training_plan_version_id
              where version.status = 'published'
                and session.scheduled_date between date_trunc('week', current_date)::date
                  and (date_trunc('week', current_date)::date + 6)
            )
            select
              count(*)::integer,
              count(outcome.id) filter (where outcome.execution_status in (
                'completed_as_planned', 'completed_modified', 'valid_substitution'))::integer,
              coalesce(sum(load.distance_m), 0)::numeric,
              coalesce(sum(load.duration_seconds), 0)::numeric,
              coalesce(sum(load.srpe_load), 0)::numeric
            from current_sessions current_session
            left join app.planned_session_outcomes outcome
              on outcome.planned_session_id = current_session.id
            left join app.v_logical_session_srpe load
              on load.planned_session_id = current_session.id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new DashboardCurrentWeekResponse(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetDecimal(2),
            reader.GetDecimal(3),
            reader.GetDecimal(4));
    }

    private static async Task<DashboardRecoveryResponse?> ReadLatestRecoveryAsync(
        OwnerDbSession session,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            select checkin.id, checkin.planned_session_id, checkin.checkin_window,
              checkin.pain, checkin.pain_location, checkin.gait_changed,
              checkin.fatigue, checkin.sleep_quality, checkin.perceived_recovery,
              checkin.has_illness_or_symptom, checkin.symptom_note,
              checkin.recovery_response, checkin.recorded_at,
              session.training_plan_version_id
            from app.session_checkins checkin
            left join app.planned_sessions session
              on session.owner_id = checkin.owner_id
             and session.id = checkin.planned_session_id
            order by checkin.recorded_at desc, checkin.id desc
            limit 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        Guid? plannedSessionId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
        Guid? planVersionId = reader.IsDBNull(13) ? null : reader.GetGuid(13);
        var href = plannedSessionId is not null && planVersionId is not null
            ? $"/plan?version={planVersionId}&session={plannedSessionId}"
            : null;
        return new DashboardRecoveryResponse(
            reader.GetGuid(0),
            plannedSessionId,
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetDecimal(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetBoolean(5),
            reader.IsDBNull(6) ? null : reader.GetDecimal(6),
            reader.IsDBNull(7) ? null : reader.GetDecimal(7),
            reader.IsDBNull(8) ? null : reader.GetDecimal(8),
            reader.IsDBNull(9) ? null : reader.GetBoolean(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.GetFieldValue<DateTimeOffset>(12),
            href);
    }

    private static async Task<SortedDictionary<DateOnly, MutableTrendWeek>> ReadTrendWeeksAsync(
        OwnerDbSession session,
        int weeks,
        CancellationToken cancellationToken)
    {
        var result = new SortedDictionary<DateOnly, MutableTrendWeek>();
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            with requested_weeks as (
              select generate_series(
                date_trunc('week', current_date)::date - ((@weeks - 1) * 7),
                date_trunc('week', current_date)::date,
                interval '7 days')::date as week_start
            ), latest_evaluation as (
              select distinct on (evaluation.week_start)
                evaluation.week_start, evaluation.id, evaluation.traffic_light
              from app.weekly_evaluations evaluation
              order by evaluation.week_start, evaluation.created_at desc, evaluation.id desc
            )
            select requested.week_start, evaluation.id, evaluation.traffic_light,
              metric.numeric_value
            from requested_weeks requested
            left join latest_evaluation evaluation on evaluation.week_start = requested.week_start
            left join app.weekly_metric_values metric
              on metric.weekly_evaluation_id = evaluation.id
             and metric.metric_code = 'P4'
             and metric.dimension = 'total'
            order by requested.week_start;
            """;
        command.Parameters.AddWithValue("weeks", weeks);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var weekStart = reader.GetFieldValue<DateOnly>(0);
            result[weekStart] = new MutableTrendWeek(
                weekStart,
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetDecimal(3));
        }

        return result;
    }

    private static async Task ReadModalitiesAsync(
        OwnerDbSession session,
        IReadOnlyDictionary<DateOnly, MutableTrendWeek> weeks,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            select week_start,
              case when modality = 'treadmill' then 'treadmill'
                   when modality = 'outdoor' then 'outdoor'
                   else 'other' end as modality_group,
              sum(activity_count)::integer,
              sum(distance_m)::numeric,
              sum(duration_seconds)::numeric
            from app.v_weekly_running
            where week_start between @start and @end
            group by week_start, modality_group
            order by week_start, modality_group;
            """;
        command.Parameters.AddWithValue("start", weeks.Keys.Min());
        command.Parameters.AddWithValue("end", weeks.Keys.Max());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var weekStart = reader.GetFieldValue<DateOnly>(0);
            if (!weeks.TryGetValue(weekStart, out var week))
            {
                continue;
            }

            decimal? distance = reader.IsDBNull(3) ? null : reader.GetDecimal(3);
            decimal? duration = reader.IsDBNull(4) ? null : reader.GetDecimal(4);
            week.Modalities.Add(new DashboardModalityTrendResponse(
                reader.GetString(1),
                reader.GetInt32(2),
                distance,
                duration,
                DashboardRules.WeightedPaceSecondsPerKm(distance, duration)));
        }
    }

    private static async Task ReadSourcesAsync(
        OwnerDbSession session,
        IReadOnlyDictionary<DateOnly, MutableTrendWeek> weeks,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            select date_trunc('week', activity.started_at_local)::date,
              session.id, session.training_plan_version_id, activity.id,
              coalesce(activity.title, activity.activity_type)
            from app.activities activity
            left join app.activity_session_links link
              on link.owner_id = activity.owner_id
             and link.activity_id = activity.id
             and link.status = 'confirmed'
            left join app.planned_sessions session
              on session.owner_id = link.owner_id
             and session.id = link.planned_session_id
            where activity.activity_category = 'running'
              and activity.started_at_local::date between @start and (@end + 6)
            order by activity.started_at_local, activity.id;
            """;
        command.Parameters.AddWithValue("start", weeks.Keys.Min());
        command.Parameters.AddWithValue("end", weeks.Keys.Max());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var weekStart = reader.GetFieldValue<DateOnly>(0);
            if (!weeks.TryGetValue(weekStart, out var week))
            {
                continue;
            }

            Guid? sessionId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
            Guid? versionId = reader.IsDBNull(2) ? null : reader.GetGuid(2);
            var activityId = reader.GetGuid(3);
            week.Sources.Add(new DashboardSourceResponse(
                sessionId,
                activityId,
                reader.GetString(4),
                sessionId is not null && versionId is not null
                    ? $"/plan?version={versionId}&session={sessionId}"
                    : $"/activities?activity={activityId}"));
        }
    }

    private static async Task<IReadOnlyList<DashboardPillarResponse>> ReadPillarsAsync(
        OwnerDbSession session,
        CancellationToken cancellationToken)
    {
        var result = new List<DashboardPillarResponse>();
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            with latest as (
              select id
              from app.weekly_evaluations
              order by week_start desc, created_at desc, id desc
              limit 1
            )
            select metric.metric_code, count(*)::integer,
              count(*) filter (where metric.status in ('missing', 'not_applicable'))::integer,
              evaluation.id
            from latest
            join app.weekly_evaluations evaluation on evaluation.id = latest.id
            join app.weekly_metric_values metric on metric.weekly_evaluation_id = evaluation.id
            group by metric.metric_code, evaluation.id
            order by metric.metric_code;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var evaluationId = reader.GetGuid(3);
            result.Add(new DashboardPillarResponse(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                evaluationId,
                $"/evaluations?evaluation={evaluationId}"));
        }

        return result;
    }

    private static async Task<List<DashboardAlertResponse>> ReadPendingAlertsAsync(
        OwnerDbSession session,
        CancellationToken cancellationToken)
    {
        var result = new List<DashboardAlertResponse>();
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            with latest_evaluation as (
              select id, traffic_light, week_start
              from app.weekly_evaluations
              order by week_start desc, created_at desc, id desc
              limit 1
            ), missing_checkins as (
              select session.id, session.training_plan_version_id, session.scheduled_date,
                case
                  when immediate.id is null then 'immediate'
                  when h24.id is null then '24h'
                  when h48.id is null then '48h'
                end as missing_window
              from app.planned_sessions session
              join app.planned_session_outcomes outcome
                on outcome.owner_id = session.owner_id
               and outcome.planned_session_id = session.id
               and outcome.execution_status in (
                 'completed_as_planned', 'completed_modified', 'valid_substitution')
              left join app.session_checkins immediate
                on immediate.planned_session_id = session.id and immediate.checkin_window = 'immediate'
              left join app.session_checkins h24
                on h24.planned_session_id = session.id and h24.checkin_window = '24h'
              left join app.session_checkins h48
                on h48.planned_session_id = session.id and h48.checkin_window = '48h'
              where session.scheduled_date >= current_date - 7
                and (immediate.id is null or h24.id is null or h48.id is null)
              order by session.scheduled_date desc
              limit 5
            ), stale_ingestion as (
              select count(*)::integer as stale_count
              from app.ingestion_runs
              where (status = 'running' and lease_until < now())
                 or (status = 'pending' and created_at < now() - interval '10 minutes')
            ), open_quarantine as (
              select count(*)::integer as open_count
              from app.quarantine_cases
              where status = 'open'
            )
            select 'evaluation' as kind,
              case when traffic_light = 'red' then 'danger' else 'warning' end as severity,
              'La última evaluación semanal está en semáforo ' || traffic_light || '.' as message,
              '/evaluations?evaluation=' || id::text as href
            from latest_evaluation
            where traffic_light in ('yellow', 'red')
            union all
            select 'checkin', 'warning',
              'Falta el check-in ' || missing_window || ' de una sesión completada.',
              '/plan?version=' || training_plan_version_id::text || '&session=' || id::text
            from missing_checkins
            union all
            select 'ingestion', 'danger',
              stale_count::text || ' trabajo(s) de ingestión esperan recuperación de heartbeat.',
              '/activities'
            from stale_ingestion
            where stale_count > 0
            union all
            select 'quarantine', 'warning',
              open_count::text || ' caso(s) de cuarentena requieren revisión.',
              '/activities'
            from open_quarantine
            where open_count > 0;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DashboardAlertResponse(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return result;
    }

    private static async Task<DashboardQuotaResponse> ReadQuotaAsync(
        OwnerDbSession session,
        FreeTierQuotaGuard guard,
        FreeTierQuotaOptions options,
        ICollection<DashboardAlertResponse> alerts,
        CancellationToken cancellationToken)
    {
        long databaseBytes;
        long storageBytes;
        long sampleCount;
        long sampleTableBytes;
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = "select * from app.current_quota_usage();";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            databaseBytes = reader.GetInt64(0);
            storageBytes = reader.GetInt64(1);
            sampleCount = reader.GetInt64(2);
            sampleTableBytes = reader.GetInt64(3);
        }

        decimal? egressGb = null;
        decimal? ciMinutes = null;
        decimal? backendHours = null;
        DateTimeOffset? measuredAt = null;
        await using (var command = session.Connection.CreateCommand())
        {
            command.Transaction = session.Transaction;
            command.CommandText = """
                select nullif(detail ->> 'egressGb', '')::numeric,
                  nullif(detail ->> 'ciMinutes', '')::numeric,
                  nullif(detail ->> 'backendHours', '')::numeric,
                  coalesce(nullif(detail ->> 'measuredAt', '')::timestamptz, occurred_at)
                from app.audit_events
                where action = 'free_tier.usage_reported'
                order by occurred_at desc, id desc
                limit 1;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                egressGb = reader.IsDBNull(0) ? null : reader.GetDecimal(0);
                ciMinutes = reader.IsDBNull(1) ? null : reader.GetDecimal(1);
                backendHours = reader.IsDBNull(2) ? null : reader.GetDecimal(2);
                measuredAt = reader.GetFieldValue<DateTimeOffset>(3);
            }
        }

        var databaseMb = decimal.Round(databaseBytes / 1024m / 1024m, 3);
        var storageMb = decimal.Round(storageBytes / 1024m / 1024m, 3);
        var resources = new[]
        {
            Resource("database", databaseMb, "MB", options.DatabaseWarningMb,
                options.DatabaseBlockMb, guard.EvaluateDatabase((int)decimal.Ceiling(databaseMb)),
                "database", null),
            Resource("storage", storageMb, "MB", options.StorageWarningMb,
                options.StorageBlockMb, guard.EvaluateStorage((int)decimal.Ceiling(storageMb)),
                "stored_objects", null),
            Resource("egress", egressGb, "GB", options.EgressWarningGb,
                options.EgressBlockGb, guard.EvaluateEgress(egressGb), "manual_provider_report", measuredAt),
            Resource("ci", ciMinutes, "minutes", options.CiWarningMinutes,
                options.CiBlockMinutes, guard.EvaluateCiMinutes(ciMinutes), "manual_provider_report", measuredAt),
            Resource("backend", backendHours, "hours", options.BackendWarningHours,
                options.BackendBlockHours, guard.EvaluateBackendHours(backendHours), "manual_provider_report", measuredAt)
        };

        foreach (var resource in resources.Where(item => item.State is "warning" or "blocked" or "not_available"))
        {
            var message = resource.State == "not_available"
                ? $"El consumo de {resource.Name} está ND; registra una lectura del proveedor antes de consumir esa cuota."
                : $"La cuota gratuita de {resource.Name} está en estado {resource.State}.";
            alerts.Add(new DashboardAlertResponse(
                "quota",
                resource.State == "blocked" ? "danger" : "warning",
                message,
                "#free-tier"));
        }

        return new DashboardQuotaResponse(
            false,
            resources,
            new ActivitySamplesReviewResponse(
                sampleCount,
                sampleTableBytes,
                10_000_000,
                5L * 1024 * 1024 * 1024,
                false,
                "El dashboard usa agregados; activity_samples no se lee por defecto."));
    }

    private static QuotaResourceResponse Resource(
        string name,
        decimal? used,
        string unit,
        decimal warning,
        decimal block,
        QuotaDecision decision,
        string source,
        DateTimeOffset? measuredAt) =>
        new(name, used, unit, warning, block,
            decision.State.ToString().ToLowerInvariant(), decision.Code,
            decision.BillingEnabled, source, measuredAt);

    private sealed class MutableTrendWeek(
        DateOnly weekStart,
        Guid? evaluationId,
        string? trafficLight,
        decimal? srpeTotal)
    {
        public DateOnly WeekStart { get; } = weekStart;
        public List<DashboardModalityTrendResponse> Modalities { get; } = [];
        public List<DashboardSourceResponse> Sources { get; } = [];

        public DashboardTrendWeekResponse ToResponse() => new(
            WeekStart,
            WeekStart.AddDays(6),
            evaluationId,
            trafficLight,
            srpeTotal,
            Modalities,
            Sources,
            evaluationId is null ? null : $"/evaluations?evaluation={evaluationId}");
    }
}

public sealed record DashboardResponse(
    DateOnly AsOf,
    int WindowWeeks,
    DashboardNextSessionResponse? NextSession,
    DashboardCurrentWeekResponse CurrentWeek,
    DashboardRecoveryResponse? LatestRecovery,
    IReadOnlyList<DashboardTrendWeekResponse> Trends,
    IReadOnlyList<DashboardPillarResponse> LatestPillars,
    IReadOnlyList<DashboardAlertResponse> Alerts,
    DashboardQuotaResponse FreeTier);

public sealed record DashboardNextSessionResponse(
    Guid Id,
    Guid PlanVersionId,
    int PlanVersionNumber,
    DateOnly ScheduledDate,
    string SessionType,
    string? Modality,
    string Obligation,
    string Objective,
    decimal? DistanceM,
    decimal? DurationSeconds,
    decimal? TargetRpeMin,
    decimal? TargetRpeMax,
    string Href);

public sealed record DashboardCurrentWeekResponse(
    int PlannedSessions,
    int CompletedSessions,
    decimal ActualDistanceM,
    decimal ActualDurationSeconds,
    decimal SrpeLoad);

public sealed record DashboardRecoveryResponse(
    Guid CheckinId,
    Guid? PlannedSessionId,
    string Window,
    decimal? Pain,
    string? PainLocation,
    bool? GaitChanged,
    decimal? Fatigue,
    decimal? SleepQuality,
    decimal? PerceivedRecovery,
    bool? HasIllnessOrSymptom,
    string? SymptomNote,
    string? RecoveryResponse,
    DateTimeOffset RecordedAt,
    string? Href);

public sealed record DashboardTrendWeekResponse(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    Guid? EvaluationId,
    string? TrafficLight,
    decimal? SrpeTotal,
    IReadOnlyList<DashboardModalityTrendResponse> Modalities,
    IReadOnlyList<DashboardSourceResponse> Sources,
    string? EvaluationHref);

public sealed record DashboardModalityTrendResponse(
    string Modality,
    int ActivityCount,
    decimal? DistanceM,
    decimal? DurationSeconds,
    decimal? PaceSecondsPerKm);

public sealed record DashboardSourceResponse(
    Guid? PlannedSessionId,
    Guid ActivityId,
    string Label,
    string Href);

public sealed record DashboardPillarResponse(
    string Pillar,
    int MetricCount,
    int MissingCount,
    Guid EvaluationId,
    string Href);

public sealed record DashboardAlertResponse(
    string Kind,
    string Severity,
    string Message,
    string? Href);

public sealed record DashboardQuotaResponse(
    bool BillingEnabled,
    IReadOnlyList<QuotaResourceResponse> Resources,
    ActivitySamplesReviewResponse ActivitySamples);

public sealed record QuotaResourceResponse(
    string Name,
    decimal? Used,
    string Unit,
    decimal WarningAt,
    decimal BlockAt,
    string State,
    string Code,
    bool BillingEnabled,
    string Source,
    DateTimeOffset? MeasuredAt);

public sealed record ActivitySamplesReviewResponse(
    long RowCount,
    long TableBytes,
    long PartitionReviewRowThreshold,
    long PartitionReviewByteThreshold,
    bool ReadByDashboard,
    string Rationale);
