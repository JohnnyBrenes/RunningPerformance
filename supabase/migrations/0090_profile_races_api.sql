create or replace function app.create_race_goal_version(
  target_race_id_value uuid,
  goal_time_seconds_value numeric,
  goal_pace_seconds_per_km_value numeric,
  confidence_value text,
  rationale_value text,
  correlation_id_value uuid)
returns app.race_goal_versions
language plpgsql
security definer
set search_path = ''
as $$
declare
  owner_id_value uuid := app.current_owner_id();
  previous_goal app.race_goal_versions%rowtype;
  new_goal app.race_goal_versions%rowtype;
  next_version integer;
begin
  if owner_id_value is null then
    raise insufficient_privilege using message = 'Authenticated owner context is required.';
  end if;

  perform 1
  from app.target_races
  where owner_id = owner_id_value
    and id = target_race_id_value
  for update;

  if not found then
    raise no_data_found using message = 'Race not found.';
  end if;

  select *
  into previous_goal
  from app.race_goal_versions
  where owner_id = owner_id_value
    and target_race_id = target_race_id_value
    and is_current
  for update;

  if found then
    next_version := previous_goal.version_number + 1;
    update app.race_goal_versions
    set is_current = false
    where owner_id = owner_id_value
      and id = previous_goal.id;
  else
    next_version := 1;
  end if;

  insert into app.race_goal_versions (
    owner_id,
    target_race_id,
    version_number,
    goal_time_seconds,
    goal_pace_seconds_per_km,
    confidence,
    rationale,
    supersedes_id,
    is_current)
  values (
    owner_id_value,
    target_race_id_value,
    next_version,
    goal_time_seconds_value,
    goal_pace_seconds_per_km_value,
    confidence_value,
    rationale_value,
    previous_goal.id,
    true)
  returning * into new_goal;

  insert into app.audit_events (
    owner_id,
    actor_id,
    actor_type,
    action,
    entity_type,
    entity_id,
    correlation_id,
    changed_fields,
    detail)
  values (
    owner_id_value,
    owner_id_value,
    'athlete',
    'race_goal.version_created',
    'race_goal_version',
    new_goal.id,
    correlation_id_value,
    array['goal_time_seconds','goal_pace_seconds_per_km','confidence','rationale'],
    jsonb_build_object(
      'target_race_id', target_race_id_value,
      'version_number', next_version));

  return new_goal;
end
$$;

revoke all on function app.create_race_goal_version(uuid, numeric, numeric, text, text, uuid)
from public, anon, authenticated, rp_worker;
grant execute on function app.create_race_goal_version(uuid, numeric, numeric, text, text, uuid)
to rp_api;
