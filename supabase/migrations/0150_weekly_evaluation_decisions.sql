-- APP-011 / I7: immutable weekly P1-P5 snapshots, explainable safety
-- precedence and human-confirmed, versioned plan adjustments.

alter table app.weekly_metric_values
  drop constraint weekly_metric_values_check,
  alter column status set not null,
  alter column formula_version set not null,
  add constraint weekly_metric_values_status_check
    check (status in ('available', 'partial', 'missing', 'not_applicable')),
  add constraint weekly_metric_values_typed_value_check check (
    (status in ('missing', 'not_applicable')
      and num_nonnulls(numeric_value, boolean_value, text_value) = 0)
    or
    (status in ('available', 'partial')
      and num_nonnulls(numeric_value, boolean_value, text_value) = 1));

alter table app.weekly_decisions
  add constraint weekly_decisions_decision_check
    check (decision in ('execute_plan', 'adapt', 'reduce', 'stop_and_assess')),
  add constraint weekly_decisions_one_per_evaluation
    unique (weekly_evaluation_id),
  add constraint weekly_decisions_confirmed_by_owner_check
    check (confirmed_by = owner_id);

alter table app.plan_adjustments
  add constraint plan_adjustments_distinct_versions_check
    check (source_plan_version_id <> target_plan_version_id),
  add constraint plan_adjustments_target_type_check
    check (target_type = 'planned_session'),
  add constraint plan_adjustments_type_check
    check (adjustment_type in ('reschedule', 'objective', 'reschedule_and_objective'));

alter table app.weekly_evaluation_sessions
  add constraint weekly_evaluation_sessions_classification_check
    check (classification in ('planned', 'unplanned_activity')),
  add constraint weekly_evaluation_sessions_execution_status_check
    check (execution_status is null or execution_status in (
      'completed_as_planned',
      'completed_modified',
      'valid_substitution',
      'not_completed',
      'optional_not_completed'));

create index weekly_metric_values_evaluation_code
  on app.weekly_metric_values(weekly_evaluation_id, metric_code, dimension);
create index weekly_metric_evidence_metric
  on app.weekly_metric_evidence(weekly_metric_value_id, id);
create index weekly_decisions_evaluation
  on app.weekly_decisions(weekly_evaluation_id);

-- Snapshots and confirmed decisions are append-only. A new cutoff creates a new
-- provisional snapshot; the partial unique index in 0060 still permits one final.
revoke update, delete on
  app.weekly_evaluations,
  app.weekly_evaluation_sessions,
  app.weekly_metric_values,
  app.weekly_metric_evidence,
  app.weekly_decisions,
  app.plan_adjustments
from rp_api, rp_worker;

drop policy owner_insert on app.weekly_decisions;
create policy owner_insert on app.weekly_decisions
  for insert to rp_api, rp_worker
  with check (app.owns(owner_id) and confirmed_by = app.current_owner_id());

create or replace function app.create_weekly_evaluation_snapshot(
  week_start_value date,
  status_value text,
  correlation_id_value uuid)
returns uuid
language plpgsql
security definer
set search_path = ''
as $$
declare
  owner_id_value uuid := app.current_owner_id();
  plan_version_id_value uuid;
  evaluation_id_value uuid;
  traffic_light_value text;
  rationale_value text;
  week_end_value date := week_start_value + 6;
  has_red boolean;
  has_yellow boolean;
  has_unconfirmed boolean;
  has_missing_key_response boolean;
begin
  if owner_id_value is null then
    raise insufficient_privilege using message = 'Authenticated owner context is required.';
  end if;
  if extract(isodow from week_start_value) <> 1 then
    raise check_violation using message = 'Weekly evaluations must start on Monday.';
  end if;
  if status_value not in ('provisional', 'final') then
    raise check_violation using message = 'Evaluation status must be provisional or final.';
  end if;

  select version.id into plan_version_id_value
  from app.training_plan_versions version
  where version.owner_id = owner_id_value
    and version.status in ('published', 'superseded')
    and exists (
      select 1
      from app.planned_sessions session
      where session.owner_id = version.owner_id
        and session.training_plan_version_id = version.id
        and session.scheduled_date between week_start_value and week_end_value)
  order by (version.status = 'published') desc, version.version_number desc
  limit 1;

  if plan_version_id_value is null then
    raise no_data_found using message = 'No published plan version covers the requested week.';
  end if;

  select exists (
    select 1
    from app.planned_sessions session
    join app.session_checkins checkin
      on checkin.owner_id = session.owner_id
     and checkin.planned_session_id = session.id
    where session.owner_id = owner_id_value
      and session.training_plan_version_id = plan_version_id_value
      and session.scheduled_date between week_start_value and week_end_value
      and (
        checkin.gait_changed is true
        or checkin.has_illness_or_symptom is true
        or checkin.recovery_response = 'adverse'))
  into has_red;

  select exists (
    select 1
    from app.planned_sessions session
    join app.session_checkins checkin
      on checkin.owner_id = session.owner_id
     and checkin.planned_session_id = session.id
    where session.owner_id = owner_id_value
      and session.training_plan_version_id = plan_version_id_value
      and session.scheduled_date between week_start_value and week_end_value
      and (
        checkin.pain > 0
        or checkin.fatigue >= 7
        or checkin.sleep_quality <= 2
        or checkin.perceived_recovery <= 4
        or checkin.recovery_response = 'incomplete'))
  into has_yellow;

  select exists (
    select 1
    from app.planned_sessions session
    left join app.planned_session_outcomes outcome
      on outcome.owner_id = session.owner_id
     and outcome.planned_session_id = session.id
    where session.owner_id = owner_id_value
      and session.training_plan_version_id = plan_version_id_value
      and session.scheduled_date between week_start_value and week_end_value
      and session.obligation <> 'optional'
      and outcome.id is null)
  into has_unconfirmed;

  select exists (
    select 1
    from app.planned_sessions session
    join app.planned_session_outcomes outcome
      on outcome.owner_id = session.owner_id
     and outcome.planned_session_id = session.id
     and outcome.execution_status in (
       'completed_as_planned', 'completed_modified', 'valid_substitution')
    where session.owner_id = owner_id_value
      and session.training_plan_version_id = plan_version_id_value
      and session.scheduled_date between week_start_value and week_end_value
      and (
        session.session_type in ('long_run', 'quality', 'strength_mobility_plyometrics')
        or session.session_type like '%plyometric%')
      and not exists (
        select 1
        from app.session_checkins response
        where response.owner_id = session.owner_id
          and response.planned_session_id = session.id
          and response.checkin_window in ('24h', '48h')))
  into has_missing_key_response;

  traffic_light_value := case
    when has_red then 'red'
    when has_yellow or has_unconfirmed or has_missing_key_response then 'yellow'
    else 'green'
  end;

  rationale_value := case traffic_light_value
    when 'red' then
      'Rojo: prevalece una señal de seguridad (cambio de marcha, enfermedad/síntoma o respuesta adversa).'
    when 'yellow' then concat_ws(' ',
      'Amarillo: la semana requiere revisión antes de progresar.',
      case when has_yellow then 'Hay dolor o una señal desfavorable de fatiga, sueño, recuperación o respuesta posterior.' end,
      case when has_unconfirmed then 'Persisten sesiones obligatorias sin resultado confirmado.' end,
      case when has_missing_key_response then 'Falta respuesta de 24–48 h de una sesión clave.' end)
    else
      'Verde: no hay señales de seguridad adversas ni resultados obligatorios o respuestas clave pendientes.'
  end;

  insert into app.weekly_evaluations (
    owner_id, week_start, format_version, plan_version_id, cutoff_at,
    status, traffic_light, rationale)
  values (
    owner_id_value, week_start_value, 'TRN-003-v1-2026-08-11',
    plan_version_id_value, now(), status_value, traffic_light_value,
    rationale_value)
  returning id into evaluation_id_value;

  insert into app.weekly_evaluation_sessions (
    owner_id, weekly_evaluation_id, planned_session_id,
    classification, execution_status)
  select
    owner_id_value, evaluation_id_value, session.id, 'planned',
    outcome.execution_status
  from app.planned_sessions session
  left join app.planned_session_outcomes outcome
    on outcome.owner_id = session.owner_id
   and outcome.planned_session_id = session.id
  where session.owner_id = owner_id_value
    and session.training_plan_version_id = plan_version_id_value
    and session.scheduled_date between week_start_value and week_end_value;

  -- P1: strict completion and all five TRN-003 outcomes remain separate by type.
  with session_types as (
    select distinct session.session_type
    from app.planned_sessions session
    where session.owner_id = owner_id_value
      and session.training_plan_version_id = plan_version_id_value
      and session.scheduled_date between week_start_value and week_end_value
  ), outcome_values(execution_status) as (
    values
      ('completed_as_planned'::text),
      ('completed_modified'),
      ('valid_substitution'),
      ('not_completed'),
      ('optional_not_completed')
  )
  insert into app.weekly_metric_values (
    owner_id, weekly_evaluation_id, metric_code, dimension, numeric_value,
    unit, status, formula_version)
  select
    owner_id_value, evaluation_id_value, 'P1',
    session_types.session_type || ':' || outcome_values.execution_status,
    count(outcome.id)::numeric, 'sessions', 'available',
    'P1-count-v1'
  from session_types
  cross join outcome_values
  left join app.planned_sessions session
    on session.owner_id = owner_id_value
   and session.training_plan_version_id = plan_version_id_value
   and session.scheduled_date between week_start_value and week_end_value
   and session.session_type = session_types.session_type
  left join app.planned_session_outcomes outcome
    on outcome.owner_id = session.owner_id
   and outcome.planned_session_id = session.id
   and outcome.execution_status = outcome_values.execution_status
  group by session_types.session_type, outcome_values.execution_status;

  insert into app.weekly_metric_values (
    owner_id, weekly_evaluation_id, metric_code, dimension, numeric_value,
    unit, status, formula_version)
  select
    owner_id_value, evaluation_id_value, 'P1',
    session.session_type || ':strict_completion_percent',
    case when count(*) filter (where session.obligation <> 'optional') = 0
      then null
      else round(
        100.0 * count(*) filter (
          where session.obligation <> 'optional'
            and outcome.execution_status = 'completed_as_planned')
        / count(*) filter (where session.obligation <> 'optional'), 2)
    end,
    'percent',
    case when count(*) filter (where session.obligation <> 'optional') = 0
      then 'not_applicable' else 'available' end,
    'P1-strict-v1'
  from app.planned_sessions session
  left join app.planned_session_outcomes outcome
    on outcome.owner_id = session.owner_id
   and outcome.planned_session_id = session.id
  where session.owner_id = owner_id_value
    and session.training_plan_version_id = plan_version_id_value
    and session.scheduled_date between week_start_value and week_end_value
  group by session.session_type;

  insert into app.weekly_metric_values (
    owner_id, weekly_evaluation_id, metric_code, dimension, numeric_value,
    unit, status, formula_version)
  select
    owner_id_value, evaluation_id_value, 'P1',
    session.session_type || ':unconfirmed',
    count(*) filter (where outcome.id is null)::numeric,
    'sessions', 'available', 'P1-unconfirmed-v1'
  from app.planned_sessions session
  left join app.planned_session_outcomes outcome
    on outcome.owner_id = session.owner_id
   and outcome.planned_session_id = session.id
  where session.owner_id = owner_id_value
    and session.training_plan_version_id = plan_version_id_value
    and session.scheduled_date between week_start_value and week_end_value
  group by session.session_type;

  -- P2: plan and execution are explicit; treadmill and outdoor never mix pace.
  with stats as (
    select
      sum(session.distance_m) as planned_distance_m,
      sum(session.duration_seconds) as planned_duration_seconds,
      count(*) filter (where session.distance_m is not null) as planned_distance_count,
      count(*) filter (where session.duration_seconds is not null) as planned_duration_count,
      count(*) as planned_count
    from app.planned_sessions session
    where session.owner_id = owner_id_value
      and session.training_plan_version_id = plan_version_id_value
      and session.scheduled_date between week_start_value and week_end_value
      and (session.modality = 'running' or session.session_type like '%run%')
  ), actual as (
    select
      coalesce(activity.modality, 'unknown') as modality,
      sum(activity.distance_m) as distance_m,
      sum(activity.duration_seconds) as duration_seconds,
      count(*) filter (where activity.distance_m is not null) as distance_count,
      count(*) filter (where activity.duration_seconds is not null) as duration_count,
      count(*) as activity_count
    from app.planned_sessions session
    join app.activity_session_links link
      on link.owner_id = session.owner_id
     and link.planned_session_id = session.id
     and link.status = 'confirmed'
    join app.activities activity
      on activity.owner_id = link.owner_id
     and activity.id = link.activity_id
     and (activity.activity_category = 'running' or activity.activity_type = 'running')
    where session.owner_id = owner_id_value
      and session.training_plan_version_id = plan_version_id_value
      and session.scheduled_date between week_start_value and week_end_value
    group by coalesce(activity.modality, 'unknown')
  ), combined as (
    select
      (select planned_distance_m from stats) as planned_distance_m,
      (select planned_duration_seconds from stats) as planned_duration_seconds,
      (select planned_distance_count from stats) as planned_distance_count,
      (select planned_duration_count from stats) as planned_duration_count,
      (select planned_count from stats) as planned_count,
      sum(distance_m) as actual_distance_all,
      sum(duration_seconds) as actual_duration_all,
      sum(distance_m) filter (where modality = 'treadmill') as actual_distance_treadmill,
      sum(duration_seconds) filter (where modality = 'treadmill') as actual_duration_treadmill,
      sum(distance_m) filter (where modality = 'outdoor') as actual_distance_outdoor,
      sum(duration_seconds) filter (where modality = 'outdoor') as actual_duration_outdoor
    from actual
  )
  insert into app.weekly_metric_values (
    owner_id, weekly_evaluation_id, metric_code, dimension, numeric_value,
    unit, status, formula_version)
  select owner_id_value, evaluation_id_value, 'P2', value.dimension,
    value.numeric_value, value.unit,
    case when value.numeric_value is null then 'missing' else 'available' end,
    'P2-time-distance-v1'
  from combined
  cross join lateral (values
    ('planned_distance_m', planned_distance_m, 'm'),
    ('planned_duration_seconds', planned_duration_seconds, 's'),
    ('actual_distance_m:all', actual_distance_all, 'm'),
    ('actual_duration_seconds:all', actual_duration_all, 's'),
    ('actual_distance_m:treadmill', actual_distance_treadmill, 'm'),
    ('actual_duration_seconds:treadmill', actual_duration_treadmill, 's'),
    ('actual_distance_m:outdoor', actual_distance_outdoor, 'm'),
    ('actual_duration_seconds:outdoor', actual_duration_outdoor, 's'),
    ('pace_seconds_per_km:all',
      case when actual_distance_all > 0
        then actual_duration_all / actual_distance_all * 1000 end, 's/km'),
    ('pace_seconds_per_km:treadmill',
      case when actual_distance_treadmill > 0
        then actual_duration_treadmill / actual_distance_treadmill * 1000 end, 's/km'),
    ('pace_seconds_per_km:outdoor',
      case when actual_distance_outdoor > 0
        then actual_duration_outdoor / actual_distance_outdoor * 1000 end, 's/km')
  ) as value(dimension, numeric_value, unit);

  -- P3: an explicit long-run observation plus its independent components.
  with long_run as (
    select
      session.id as planned_session_id,
      outcome.execution_status,
      load.distance_m,
      load.duration_seconds,
      load.session_rpe,
      (select sum(activity.elevation_gain_m)
       from app.activity_session_links link
       join app.activities activity
         on activity.owner_id = link.owner_id and activity.id = link.activity_id
       where link.owner_id = session.owner_id
         and link.planned_session_id = session.id
         and link.status = 'confirmed') as elevation_gain_m,
      (select max(checkin.pain) from app.session_checkins checkin
       where checkin.owner_id = session.owner_id
         and checkin.planned_session_id = session.id) as pain,
      (select bool_or(checkin.gait_changed) from app.session_checkins checkin
       where checkin.owner_id = session.owner_id
         and checkin.planned_session_id = session.id) as gait_changed,
      (select case max(case checkin.recovery_response
          when 'adverse' then 3 when 'incomplete' then 2 when 'normal' then 1 end)
        when 3 then 'adverse' when 2 then 'incomplete' when 1 then 'normal' end
       from app.session_checkins checkin
       where checkin.owner_id = session.owner_id
         and checkin.planned_session_id = session.id) as recovery_response
    from app.planned_sessions session
    left join app.planned_session_outcomes outcome
      on outcome.owner_id = session.owner_id
     and outcome.planned_session_id = session.id
    left join app.v_logical_session_srpe load
      on load.owner_id = session.owner_id
     and load.planned_session_id = session.id
    where session.owner_id = owner_id_value
      and session.training_plan_version_id = plan_version_id_value
      and session.scheduled_date between week_start_value and week_end_value
      and session.session_type = 'long_run'
    order by session.scheduled_date, session.id
    limit 1
  )
  insert into app.weekly_metric_values (
    owner_id, weekly_evaluation_id, metric_code, dimension, text_value,
    status, formula_version)
  select owner_id_value, evaluation_id_value, 'P3', 'outdoor_long_run_observation',
    case when exists (select 1 from long_run) then (
      select concat(
        'Estado ', coalesce(execution_status, 'ND'),
        '; distancia ', coalesce(round(distance_m / 1000.0, 2)::text, 'ND'), ' km',
        '; duración ', coalesce(round(duration_seconds / 60.0, 1)::text, 'ND'), ' min',
        '; RPE ', coalesce(session_rpe::text, 'ND'),
        '; respuesta 24–48 h ', coalesce(recovery_response, 'ND'), '.')
      from long_run)
    end,
    case when exists (select 1 from long_run) then 'available' else 'missing' end,
    'P3-observation-v1';

  with long_run as (
    select load.distance_m, load.duration_seconds, load.session_rpe,
      (select sum(activity.elevation_gain_m)
       from app.activity_session_links link
       join app.activities activity
         on activity.owner_id = link.owner_id and activity.id = link.activity_id
       where link.owner_id = session.owner_id
         and link.planned_session_id = session.id
         and link.status = 'confirmed') as elevation_gain_m,
      (select max(checkin.pain) from app.session_checkins checkin
       where checkin.owner_id = session.owner_id
         and checkin.planned_session_id = session.id) as pain
    from app.planned_sessions session
    left join app.v_logical_session_srpe load
      on load.owner_id = session.owner_id and load.planned_session_id = session.id
    where session.owner_id = owner_id_value
      and session.training_plan_version_id = plan_version_id_value
      and session.scheduled_date between week_start_value and week_end_value
      and session.session_type = 'long_run'
    order by session.scheduled_date, session.id limit 1
  )
  insert into app.weekly_metric_values (
    owner_id, weekly_evaluation_id, metric_code, dimension, numeric_value,
    unit, status, formula_version)
  select owner_id_value, evaluation_id_value, 'P3', value.dimension,
    value.numeric_value, value.unit,
    case when value.numeric_value is null then 'missing' else 'available' end,
    'P3-components-v1'
  from (select 1) seed
  left join long_run on true
  cross join lateral (values
    ('distance_m', distance_m, 'm'),
    ('duration_seconds', duration_seconds, 's'),
    ('elevation_gain_m', elevation_gain_m, 'm'),
    ('session_rpe', session_rpe, 'RPE'),
    ('pain', pain, '0-10')
  ) as value(dimension, numeric_value, unit);

  -- P4: the logical-session view prevents split activities from double counting.
  insert into app.weekly_metric_values (
    owner_id, weekly_evaluation_id, metric_code, dimension, numeric_value,
    unit, status, formula_version)
  select owner_id_value, evaluation_id_value, 'P4',
    'session:' || session.id::text, load.srpe_load, 'AU',
    case when load.srpe_load is null then 'missing' else 'available' end,
    'P4-duration-minutes-times-RPE-v1'
  from app.planned_sessions session
  left join app.v_logical_session_srpe load
    on load.owner_id = session.owner_id and load.planned_session_id = session.id
  where session.owner_id = owner_id_value
    and session.training_plan_version_id = plan_version_id_value
    and session.scheduled_date between week_start_value and week_end_value;

  with loads as (
    select session.id, session.session_type, session.modality, load.srpe_load
    from app.planned_sessions session
    left join app.v_logical_session_srpe load
      on load.owner_id = session.owner_id and load.planned_session_id = session.id
    where session.owner_id = owner_id_value
      and session.training_plan_version_id = plan_version_id_value
      and session.scheduled_date between week_start_value and week_end_value
  ), groups as (
    select 'running'::text as dimension,
      sum(srpe_load) filter (where modality = 'running' or session_type like '%run%') as value,
      count(*) filter (where modality = 'running' or session_type like '%run%') as expected,
      count(srpe_load) filter (where modality = 'running' or session_type like '%run%') as available
    from loads
    union all
    select 'strength_plyometrics_other',
      sum(srpe_load) filter (where not (modality = 'running' or session_type like '%run%')),
      count(*) filter (where not (modality = 'running' or session_type like '%run%')),
      count(srpe_load) filter (where not (modality = 'running' or session_type like '%run%'))
    from loads
    union all
    select 'total', sum(srpe_load), count(*), count(srpe_load) from loads
  )
  insert into app.weekly_metric_values (
    owner_id, weekly_evaluation_id, metric_code, dimension, numeric_value,
    unit, status, formula_version)
  select owner_id_value, evaluation_id_value, 'P4', dimension, value, 'AU',
    case when expected = 0 then 'not_applicable'
      when available = 0 then 'missing'
      when available < expected then 'partial'
      else 'available' end,
    'P4-weekly-sum-v1'
  from groups;

  -- P5 stays component-based; the worst observed value is displayed but never
  -- compensated by another component. Missing means a stored NULL, not zero.
  with checkins as (
    select checkin.*
    from app.planned_sessions session
    join app.session_checkins checkin
      on checkin.owner_id = session.owner_id
     and checkin.planned_session_id = session.id
    where session.owner_id = owner_id_value
      and session.training_plan_version_id = plan_version_id_value
      and session.scheduled_date between week_start_value and week_end_value
  ), components as (
    select 'pain'::text as dimension, max(pain)::numeric as numeric_value,
      null::boolean as boolean_value, null::text as text_value, '0-10'::text as unit
    from checkins
    union all select 'fatigue', max(fatigue), null, null, '0-10' from checkins
    union all select 'sleep_quality', min(sleep_quality), null, null, '1-5' from checkins
    union all select 'perceived_recovery', min(perceived_recovery), null, null, '0-10' from checkins
    union all select 'gait_changed', null, bool_or(gait_changed), null, null from checkins
    union all select 'illness_or_symptoms', null, bool_or(has_illness_or_symptom), null, null from checkins
    union all select 'response_24_to_48_hours', null, null,
      case max(case recovery_response when 'adverse' then 3 when 'incomplete' then 2 when 'normal' then 1 end)
        when 3 then 'adverse' when 2 then 'incomplete' when 1 then 'normal' end,
      null from checkins
  )
  insert into app.weekly_metric_values (
    owner_id, weekly_evaluation_id, metric_code, dimension,
    numeric_value, boolean_value, text_value, unit, status, formula_version)
  select owner_id_value, evaluation_id_value, 'P5', dimension,
    numeric_value, boolean_value, text_value, unit,
    case when num_nonnulls(numeric_value, boolean_value, text_value) = 0
      then 'missing' else 'available' end,
    'P5-worst-component-no-score-v1'
  from components;

  -- Every aggregate remains navigable. Planned-session evidence is the stable
  -- fallback; activity/check-in evidence adds the recorded source when present.
  insert into app.weekly_metric_evidence (
    owner_id, weekly_metric_value_id, planned_session_id)
  select owner_id_value, metric.id, session.planned_session_id
  from app.weekly_metric_values metric
  join app.weekly_evaluation_sessions session
    on session.owner_id = metric.owner_id
   and session.weekly_evaluation_id = metric.weekly_evaluation_id
   and session.planned_session_id is not null
  where metric.owner_id = owner_id_value
    and metric.weekly_evaluation_id = evaluation_id_value;

  insert into app.weekly_metric_evidence (
    owner_id, weekly_metric_value_id, activity_id)
  select distinct owner_id_value, metric.id, activity.id
  from app.weekly_metric_values metric
  join app.planned_sessions session
    on session.owner_id = metric.owner_id
   and session.training_plan_version_id = plan_version_id_value
   and session.scheduled_date between week_start_value and week_end_value
  join app.activity_session_links link
    on link.owner_id = session.owner_id
   and link.planned_session_id = session.id
   and link.status = 'confirmed'
  join app.activities activity
    on activity.owner_id = link.owner_id and activity.id = link.activity_id
  where metric.owner_id = owner_id_value
    and metric.weekly_evaluation_id = evaluation_id_value
    and metric.metric_code in ('P2', 'P3', 'P4');

  insert into app.weekly_metric_evidence (
    owner_id, weekly_metric_value_id, session_checkin_id)
  select distinct owner_id_value, metric.id, checkin.id
  from app.weekly_metric_values metric
  join app.planned_sessions session
    on session.owner_id = metric.owner_id
   and session.training_plan_version_id = plan_version_id_value
   and session.scheduled_date between week_start_value and week_end_value
  join app.session_checkins checkin
    on checkin.owner_id = session.owner_id
   and checkin.planned_session_id = session.id
  where metric.owner_id = owner_id_value
    and metric.weekly_evaluation_id = evaluation_id_value
    and metric.metric_code in ('P3', 'P4', 'P5');

  insert into app.audit_events (
    owner_id, actor_id, actor_type, action, entity_type, entity_id,
    correlation_id, changed_fields, detail)
  values (
    owner_id_value, owner_id_value, 'athlete',
    'weekly_evaluation.snapshot_created', 'weekly_evaluation',
    evaluation_id_value, correlation_id_value,
    array['status', 'traffic_light', 'metrics', 'evidence'],
    jsonb_build_object(
      'weekStart', week_start_value,
      'weekEnd', week_end_value,
      'formatVersion', 'TRN-003-v1-2026-08-11'));

  return evaluation_id_value;
end
$$;

grant execute on function app.create_weekly_evaluation_snapshot(date, text, uuid)
to rp_api;
