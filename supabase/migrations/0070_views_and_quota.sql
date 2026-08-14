create or replace function app.free_tier_quota_state(
  used_bytes bigint,
  warning_mb integer,
  block_mb integer
)
returns text
language sql
immutable
as $$
  select case
    when used_bytes < 0 or warning_mb < 0 or block_mb <= warning_mb then 'invalid'
    when used_bytes >= block_mb::bigint * 1024 * 1024 then 'blocked'
    when used_bytes >= warning_mb::bigint * 1024 * 1024 then 'warning'
    else 'available'
  end
$$;

create view app.v_activity_history
with (security_invoker = true)
as
select
  a.owner_id,
  a.id as activity_id,
  a.garmin_activity_id,
  a.provisional_activity_key,
  a.activity_type,
  a.activity_category,
  a.modality,
  a.started_at_local,
  a.started_at_utc,
  a.title,
  a.distance_m,
  a.duration_seconds,
  a.average_pace_seconds_per_km,
  a.validation_status
from app.activities a;

create view app.v_activity_srpe
with (security_invoker = true)
as
select
  a.owner_id,
  a.id as activity_id,
  a.duration_seconds,
  c.session_rpe,
  case
    when a.duration_seconds is not null and c.session_rpe is not null
      then round((a.duration_seconds / 60.0) * c.session_rpe, 2)
  end as srpe_load
from app.activities a
left join app.session_checkins c
  on c.owner_id = a.owner_id
 and c.activity_id = a.id
 and c.checkin_window = 'immediate';

create view app.v_planned_vs_completed
with (security_invoker = true)
as
select
  s.owner_id,
  s.id as planned_session_id,
  s.scheduled_date,
  s.session_type,
  o.execution_status,
  l.activity_id,
  l.status as link_status
from app.planned_sessions s
left join app.planned_session_outcomes o
  on o.owner_id = s.owner_id and o.planned_session_id = s.id
left join app.activity_session_links l
  on l.owner_id = s.owner_id
 and l.planned_session_id = s.id
 and l.status in ('proposed','confirmed');

create view app.v_weekly_running
with (security_invoker = true)
as
select
  a.owner_id,
  date_trunc('week', a.started_at_local)::date as week_start,
  coalesce(a.modality, 'unknown') as modality,
  count(*) as activity_count,
  sum(a.distance_m) as distance_m,
  sum(a.duration_seconds) as duration_seconds
from app.activities a
where a.activity_category = 'running'
group by a.owner_id, date_trunc('week', a.started_at_local)::date, coalesce(a.modality, 'unknown');

create view app.v_weekly_p1_to_p5_sources
with (security_invoker = true)
as
select
  e.owner_id,
  e.id as weekly_evaluation_id,
  e.week_start,
  m.metric_code,
  m.dimension,
  m.numeric_value,
  m.boolean_value,
  m.text_value,
  m.unit,
  ev.activity_id,
  ev.planned_session_id,
  ev.session_checkin_id,
  ev.source_observation_id
from app.weekly_evaluations e
join app.weekly_metric_values m
  on m.owner_id = e.owner_id and m.weekly_evaluation_id = e.id
left join app.weekly_metric_evidence ev
  on ev.owner_id = m.owner_id and ev.weekly_metric_value_id = m.id;

create view app.v_current_race_goals
with (security_invoker = true)
as
select
  r.owner_id,
  r.id as target_race_id,
  r.name,
  r.race_date,
  r.distance_m,
  r.priority,
  g.id as race_goal_version_id,
  g.version_number,
  g.goal_time_seconds,
  g.goal_pace_seconds_per_km,
  g.confidence,
  g.rationale
from app.target_races r
join app.race_goal_versions g
  on g.owner_id = r.owner_id
 and g.target_race_id = r.id
 and g.is_current;

create view app.v_current_training_plan
with (security_invoker = true)
as
select
  p.owner_id,
  p.id as training_plan_id,
  p.name,
  p.purpose,
  v.id as training_plan_version_id,
  v.version_number,
  v.period_start,
  v.period_end,
  v.published_at
from app.training_plans p
join app.training_plan_versions v
  on v.owner_id = p.owner_id
 and v.training_plan_id = p.id
 and v.status = 'published';

create view app.v_current_exercise_revisions
with (security_invoker = true)
as
select distinct on (e.owner_id, e.id)
  e.owner_id,
  e.id as exercise_id,
  e.slug,
  r.id as exercise_revision_id,
  r.version_number,
  r.display_name,
  r.brief_description,
  r.setup,
  r.execution,
  r.safety_cues
from app.exercises e
join app.exercise_revisions r
  on r.owner_id = e.owner_id and r.exercise_id = e.id
order by e.owner_id, e.id, r.version_number desc;

grant execute on function app.free_tier_quota_state(bigint, integer, integer) to rp_api, rp_worker;
grant select on
  app.v_activity_history,
  app.v_activity_srpe,
  app.v_planned_vs_completed,
  app.v_weekly_running,
  app.v_weekly_p1_to_p5_sources,
  app.v_current_race_goals,
  app.v_current_training_plan,
  app.v_current_exercise_revisions
to rp_api, rp_worker;
