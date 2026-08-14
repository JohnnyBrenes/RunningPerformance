alter table app.athlete_profiles
  add column sex text not null default 'unspecified'
  check (sex in ('female', 'male', 'unspecified'));

alter table app.exercise_media
  add column presentation_sex text not null default 'unspecified'
    check (presentation_sex in ('female', 'male', 'unspecified')),
  add column width_px integer not null default 1024 check (width_px > 0),
  add column height_px integer not null default 1024 check (height_px > 0);

create trigger exercises_set_updated_at before update on app.exercises
for each row execute function app.set_updated_at();
create trigger training_plans_set_updated_at before update on app.training_plans
for each row execute function app.set_updated_at();
create trigger planned_sessions_set_updated_at before update on app.planned_sessions
for each row execute function app.set_updated_at();

create or replace function app.reject_published_plan_content_change()
returns trigger
language plpgsql
set search_path = ''
as $$
declare
  old_version_id uuid;
  new_version_id uuid;
  version_status text;
begin
  if tg_table_name = 'planned_sessions' then
    if tg_op <> 'INSERT' then
      old_version_id := old.training_plan_version_id;
    end if;
    if tg_op <> 'DELETE' then
      new_version_id := new.training_plan_version_id;
    end if;
  elsif tg_table_name = 'planned_session_blocks' then
    if tg_op <> 'INSERT' then
      select training_plan_version_id into old_version_id
      from app.planned_sessions
      where owner_id = old.owner_id and id = old.planned_session_id;
    end if;
    if tg_op <> 'DELETE' then
      select training_plan_version_id into new_version_id
      from app.planned_sessions
      where owner_id = new.owner_id and id = new.planned_session_id;
    end if;
  elsif tg_table_name = 'planned_session_exercises' then
    if tg_op <> 'INSERT' then
      select s.training_plan_version_id into old_version_id
      from app.planned_session_blocks b
      join app.planned_sessions s
        on s.owner_id = b.owner_id and s.id = b.planned_session_id
      where b.owner_id = old.owner_id and b.id = old.planned_session_block_id;
    end if;
    if tg_op <> 'DELETE' then
      select s.training_plan_version_id into new_version_id
      from app.planned_session_blocks b
      join app.planned_sessions s
        on s.owner_id = b.owner_id and s.id = b.planned_session_id
      where b.owner_id = new.owner_id and b.id = new.planned_session_block_id;
    end if;
  end if;

  if old_version_id is not null then
    select status into version_status
    from app.training_plan_versions
    where id = old_version_id;
    if version_status <> 'draft' then
      raise check_violation using message = 'Published plan content is immutable.';
    end if;
  end if;

  if new_version_id is not null and new_version_id is distinct from old_version_id then
    select status into version_status
    from app.training_plan_versions
    where id = new_version_id;
    if version_status <> 'draft' then
      raise check_violation using message = 'Published plan content is immutable.';
    end if;
  end if;

  return case when tg_op = 'DELETE' then old else new end;
end
$$;

create trigger planned_sessions_require_draft
before insert or update or delete on app.planned_sessions
for each row execute function app.reject_published_plan_content_change();
create trigger planned_session_blocks_require_draft
before insert or update or delete on app.planned_session_blocks
for each row execute function app.reject_published_plan_content_change();
create trigger planned_session_exercises_require_draft
before insert or update or delete on app.planned_session_exercises
for each row execute function app.reject_published_plan_content_change();

create or replace function app.clone_training_plan_draft(
  training_plan_id_value uuid,
  source_version_id_value uuid,
  rationale_value text,
  correlation_id_value uuid)
returns app.training_plan_versions
language plpgsql
security definer
set search_path = ''
as $$
declare
  owner_id_value uuid := app.current_owner_id();
  source_version app.training_plan_versions%rowtype;
  new_version app.training_plan_versions%rowtype;
  source_session record;
  source_block record;
  source_exercise record;
  new_session_id uuid;
  new_block_id uuid;
  next_version integer;
begin
  if owner_id_value is null then
    raise insufficient_privilege using message = 'Authenticated owner context is required.';
  end if;

  perform 1
  from app.training_plans
  where owner_id = owner_id_value and id = training_plan_id_value
  for update;
  if not found then
    raise no_data_found using message = 'Training plan not found.';
  end if;

  select * into source_version
  from app.training_plan_versions
  where owner_id = owner_id_value
    and training_plan_id = training_plan_id_value
    and id = source_version_id_value;
  if not found then
    raise no_data_found using message = 'Training plan version not found.';
  end if;

  if exists (
    select 1 from app.training_plan_versions
    where owner_id = owner_id_value
      and training_plan_id = training_plan_id_value
      and status = 'draft') then
    raise object_not_in_prerequisite_state
      using message = 'The training plan already has a draft.';
  end if;

  select coalesce(max(version_number), 0) + 1 into next_version
  from app.training_plan_versions
  where owner_id = owner_id_value and training_plan_id = training_plan_id_value;

  insert into app.training_plan_versions (
    owner_id, training_plan_id, version_number, period_start, period_end,
    status, rationale, supersedes_id)
  values (
    owner_id_value, training_plan_id_value, next_version,
    source_version.period_start, source_version.period_end,
    'draft', trim(rationale_value), source_version.id)
  returning * into new_version;

  for source_session in
    select * from app.planned_sessions
    where owner_id = owner_id_value
      and training_plan_version_id = source_version.id
    order by scheduled_date, id
  loop
    insert into app.planned_sessions (
      owner_id, training_plan_version_id, scheduled_date, session_type,
      modality, obligation, objective, distance_m, duration_seconds,
      target_rpe_min, target_rpe_max, terrain, warmup, main_set,
      recoveries, cooldown)
    values (
      owner_id_value, new_version.id, source_session.scheduled_date,
      source_session.session_type, source_session.modality,
      source_session.obligation, source_session.objective,
      source_session.distance_m, source_session.duration_seconds,
      source_session.target_rpe_min, source_session.target_rpe_max,
      source_session.terrain, source_session.warmup, source_session.main_set,
      source_session.recoveries, source_session.cooldown)
    returning id into new_session_id;

    for source_block in
      select * from app.planned_session_blocks
      where owner_id = owner_id_value
        and planned_session_id = source_session.id
      order by position
    loop
      insert into app.planned_session_blocks (
        owner_id, planned_session_id, position, block_type,
        repeat_count, instructions)
      values (
        owner_id_value, new_session_id, source_block.position,
        source_block.block_type, source_block.repeat_count,
        source_block.instructions)
      returning id into new_block_id;

      for source_exercise in
        select * from app.planned_session_exercises
        where owner_id = owner_id_value
          and planned_session_block_id = source_block.id
        order by position
      loop
        insert into app.planned_session_exercises (
          owner_id, planned_session_block_id, exercise_revision_id, position,
          sets, repetitions_min, repetitions_max, duration_seconds,
          rest_seconds, load_value, load_unit, target_rpe, target_rir,
          tempo, side, note)
        values (
          owner_id_value, new_block_id, source_exercise.exercise_revision_id,
          source_exercise.position, source_exercise.sets,
          source_exercise.repetitions_min, source_exercise.repetitions_max,
          source_exercise.duration_seconds, source_exercise.rest_seconds,
          source_exercise.load_value, source_exercise.load_unit,
          source_exercise.target_rpe, source_exercise.target_rir,
          source_exercise.tempo, source_exercise.side, source_exercise.note);
      end loop;
    end loop;
  end loop;

  insert into app.audit_events (
    owner_id, actor_id, actor_type, action, entity_type, entity_id,
    correlation_id, changed_fields, detail)
  values (
    owner_id_value, owner_id_value, 'athlete', 'training_plan.draft_cloned',
    'training_plan_version', new_version.id, correlation_id_value,
    array['version_number','period_start','period_end','rationale','supersedes_id'],
    jsonb_build_object('training_plan_id', training_plan_id_value,
      'source_version_id', source_version.id, 'version_number', next_version));

  return new_version;
end
$$;

create or replace function app.publish_training_plan_version(
  version_id_value uuid,
  correlation_id_value uuid)
returns app.training_plan_versions
language plpgsql
security definer
set search_path = ''
as $$
declare
  owner_id_value uuid := app.current_owner_id();
  target_version app.training_plan_versions%rowtype;
  published_version app.training_plan_versions%rowtype;
begin
  if owner_id_value is null then
    raise insufficient_privilege using message = 'Authenticated owner context is required.';
  end if;

  select * into target_version
  from app.training_plan_versions
  where owner_id = owner_id_value and id = version_id_value
  for update;
  if not found then
    raise no_data_found using message = 'Training plan version not found.';
  end if;
  if target_version.status <> 'draft' then
    raise object_not_in_prerequisite_state
      using message = 'Only a draft training plan can be published.';
  end if;
  if not exists (
    select 1 from app.planned_sessions
    where owner_id = owner_id_value
      and training_plan_version_id = target_version.id) then
    raise check_violation using message = 'A plan requires at least one session before publication.';
  end if;

  update app.training_plan_versions
  set status = 'superseded'
  where owner_id = owner_id_value and status = 'published';

  update app.training_plan_versions
  set status = 'published', published_at = now()
  where owner_id = owner_id_value and id = target_version.id
  returning * into published_version;

  insert into app.audit_events (
    owner_id, actor_id, actor_type, action, entity_type, entity_id,
    correlation_id, changed_fields, detail)
  values (
    owner_id_value, owner_id_value, 'athlete', 'training_plan.version_published',
    'training_plan_version', published_version.id, correlation_id_value,
    array['status','published_at'],
    jsonb_build_object('training_plan_id', published_version.training_plan_id,
      'version_number', published_version.version_number));

  return published_version;
end
$$;

revoke all on function app.reject_published_plan_content_change() from public;
revoke all on function app.clone_training_plan_draft(uuid, uuid, text, uuid)
from public, anon, authenticated, rp_worker;
revoke all on function app.publish_training_plan_version(uuid, uuid)
from public, anon, authenticated, rp_worker;
grant execute on function app.clone_training_plan_draft(uuid, uuid, text, uuid) to rp_api;
grant execute on function app.publish_training_plan_version(uuid, uuid) to rp_api;
