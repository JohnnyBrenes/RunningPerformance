-- APP-010 / I6: make the capture scaffold conform to TRN-003 and expose
-- one logical load for a planned session even when it has several activities.

alter table app.activity_session_links
  add column updated_at timestamptz not null default now();

create trigger activity_session_links_set_updated_at
before update on app.activity_session_links
for each row execute function app.set_updated_at();

create index activity_session_links_session_active
  on app.activity_session_links(owner_id, planned_session_id, created_at)
  where status in ('proposed', 'confirmed');

alter table app.planned_session_outcomes
  drop constraint planned_session_outcomes_execution_status_check;

update app.planned_session_outcomes outcome
set execution_status = case outcome.execution_status
  when 'partially_completed' then 'completed_modified'
  when 'omitted' then 'not_completed'
  when 'not_due' then case session.obligation
    when 'optional' then 'optional_not_completed'
    else 'not_completed'
  end
  else outcome.execution_status
end
from app.planned_sessions session
where session.owner_id = outcome.owner_id
  and session.id = outcome.planned_session_id;

alter table app.planned_session_outcomes
  add constraint planned_session_outcomes_execution_status_check
    check (execution_status in (
      'completed_as_planned',
      'completed_modified',
      'valid_substitution',
      'not_completed',
      'optional_not_completed')),
  add constraint planned_session_outcomes_reason_check
    check (
      execution_status in ('completed_as_planned', 'optional_not_completed')
      or nullif(trim(modification_reason), '') is not null);

alter table app.session_checkins
  rename column illness_or_symptom to symptom_note;

alter table app.session_checkins
  add column has_illness_or_symptom boolean,
  add column recovery_response text
    check (recovery_response in ('normal', 'incomplete', 'adverse')),
  add column updated_at timestamptz not null default now(),
  add constraint session_checkins_window_values_check check (
    (checkin_window = 'immediate' and recovery_response is null)
    or (checkin_window in ('24h', '48h') and session_rpe is null)),
  add constraint session_checkins_has_value_check check (
    session_rpe is not null
    or pain is not null
    or nullif(trim(pain_location), '') is not null
    or gait_changed is not null
    or fatigue is not null
    or sleep_quality is not null
    or perceived_recovery is not null
    or has_illness_or_symptom is not null
    or nullif(trim(symptom_note), '') is not null
    or recovery_response is not null
    or nullif(trim(note), '') is not null);

create trigger session_checkins_set_updated_at
before update on app.session_checkins
for each row execute function app.set_updated_at();

create unique index session_checkins_one_planned_session_window
  on app.session_checkins(planned_session_id, checkin_window)
  where planned_session_id is not null;

create unique index session_checkins_one_unlinked_activity_window
  on app.session_checkins(activity_id, checkin_window)
  where activity_id is not null and planned_session_id is null;

drop view app.v_planned_vs_completed;

create view app.v_logical_session_srpe
with (security_invoker = true)
as
select
  session.owner_id,
  session.id as planned_session_id,
  count(activity.id)::integer as activity_count,
  sum(activity.distance_m)::numeric as distance_m,
  sum(activity.duration_seconds)::numeric as duration_seconds,
  checkin.session_rpe,
  case
    when checkin.session_rpe is not null and sum(activity.duration_seconds) is not null
      then round((sum(activity.duration_seconds) / 60.0) * checkin.session_rpe, 2)
  end as srpe_load
from app.planned_sessions session
left join app.activity_session_links link
  on link.owner_id = session.owner_id
 and link.planned_session_id = session.id
 and link.status = 'confirmed'
left join app.activities activity
  on activity.owner_id = link.owner_id
 and activity.id = link.activity_id
left join app.session_checkins checkin
  on checkin.owner_id = session.owner_id
 and checkin.planned_session_id = session.id
 and checkin.checkin_window = 'immediate'
group by session.owner_id, session.id, checkin.session_rpe;

create view app.v_planned_vs_completed
with (security_invoker = true)
as
select
  session.owner_id,
  session.id as planned_session_id,
  session.scheduled_date,
  session.session_type,
  session.modality,
  session.obligation,
  outcome.execution_status,
  outcome.modification_reason,
  outcome.confirmed_at,
  count(link.activity_id) filter (where link.status = 'proposed')::integer as proposed_activity_count,
  count(link.activity_id) filter (where link.status = 'confirmed')::integer as confirmed_activity_count,
  load.distance_m as actual_distance_m,
  load.duration_seconds as actual_duration_seconds,
  load.session_rpe,
  load.srpe_load
from app.planned_sessions session
left join app.planned_session_outcomes outcome
  on outcome.owner_id = session.owner_id
 and outcome.planned_session_id = session.id
left join app.activity_session_links link
  on link.owner_id = session.owner_id
 and link.planned_session_id = session.id
 and link.status in ('proposed', 'confirmed')
left join app.v_logical_session_srpe load
  on load.owner_id = session.owner_id
 and load.planned_session_id = session.id
group by
  session.owner_id,
  session.id,
  session.scheduled_date,
  session.session_type,
  session.modality,
  session.obligation,
  outcome.execution_status,
  outcome.modification_reason,
  outcome.confirmed_at,
  load.distance_m,
  load.duration_seconds,
  load.session_rpe,
  load.srpe_load;

grant select on app.v_logical_session_srpe, app.v_planned_vs_completed
to rp_api, rp_worker;
