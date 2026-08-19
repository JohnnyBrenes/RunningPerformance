begin;
set local role postgres;
set local search_path = extensions, public;
grant usage on schema extensions to rp_api;

select plan(20);

delete from app.audit_events
where correlation_id in (
  '11111111-5001-4501-8501-111111111111',
  '11111111-5002-4502-8502-111111111111');
delete from app.plan_adjustments adjustment
using app.training_plan_versions version
where adjustment.owner_id = version.owner_id
  and adjustment.target_plan_version_id = version.id
  and version.training_plan_id = '11111111-2000-4200-8200-111111111111'
  and version.status = 'draft';
delete from app.training_plan_versions
where training_plan_id = '11111111-2000-4200-8200-111111111111'
  and status = 'draft';

select id as source_id, version_number as source_version_number
from app.training_plan_versions
where training_plan_id = '11111111-2000-4200-8200-111111111111'
  and status = 'published'
\gset

select count(*)::integer as version_count_before
from app.training_plan_versions
where training_plan_id = '11111111-2000-4200-8200-111111111111'
\gset

select is(
  (select sex from app.athlete_profiles where owner_id = '11111111-1111-4111-8111-111111111111'),
  'male',
  'synthetic athlete A selects masculine exercise media'
);

select is(
  (select sex from app.athlete_profiles where owner_id = '22222222-2222-4222-8222-222222222222'),
  'female',
  'synthetic athlete B selects feminine exercise media'
);

select is(
  (select count(*)::integer from app.exercise_media
   where exercise_revision_id = '11111111-1101-4101-8101-111111111111'),
  2,
  'an exercise revision can retain both visual variants'
);

select ok(
  (select bool_and(
     presentation_sex in ('female', 'male')
     and width_px = 1024
     and height_px = 1024
     and length(sha256) = 64)
   from app.exercise_media
   where exercise_revision_id = '11111111-1101-4101-8101-111111111111'),
  'visual variants retain presentation, dimensions and checksum metadata'
);

select is(
  (select count(*)::integer
   from app.exercise_media
   where exercise_revision_id = '11111111-1104-4104-8104-111111111111'),
  2,
  'ankle pogos includes both profile visual variants'
);

insert into app.exercises (
  id, owner_id, slug, canonical_name, movement_pattern, equipment)
values (
  '11111111-1500-4500-8500-111111111111',
  '11111111-1111-4111-8111-111111111111',
  'ejercicio-sin-imagen-test',
  'Ejercicio temporal sin imagen',
  'test',
  'Peso corporal');

insert into app.exercise_revisions (
  id, owner_id, exercise_id, version_number, display_name,
  brief_description, setup, execution, safety_cues)
values (
  '11111111-1501-4501-8501-111111111111',
  '11111111-1111-4111-8111-111111111111',
  '11111111-1500-4500-8500-111111111111',
  1,
  'Ejercicio temporal sin imagen',
  'Valida el contrato de cero medios.',
  'Preparación textual.',
  'Ejecución textual.',
  'Seguridad textual.');

select is(
  (select count(*)::integer
   from app.exercise_media
   where exercise_revision_id = '11111111-1501-4501-8501-111111111111'),
  0,
  'exercise instructions remain valid without visual media'
);

set local role rp_api;
select set_config('request.jwt.claim.sub', '11111111-1111-4111-8111-111111111111', true);

select lives_ok(
  format(
    'select app.clone_training_plan_draft(%L::uuid, %L::uuid, %L, %L::uuid)',
    '11111111-2000-4200-8200-111111111111',
    :'source_id',
    'Synthetic adjustment under test',
    '11111111-5001-4501-8501-111111111111'),
  'owner can clone a published plan into a mutable draft'
);

select id as draft_id
from app.training_plan_versions
where training_plan_id = '11111111-2000-4200-8200-111111111111'
  and status = 'draft'
\gset

select ok(
  (select version_number = :'source_version_number'::integer + 1
     and supersedes_id = :'source_id'::uuid
   from app.training_plan_versions where id = :'draft_id'::uuid),
  'draft receives the next number and points to its source version'
);

select is(
  (select count(*)::integer from app.planned_sessions
   where training_plan_version_id = :'draft_id'::uuid),
  (select count(*)::integer from app.planned_sessions
   where training_plan_version_id = :'source_id'::uuid),
  'draft cloning preserves the ordered session set'
);

select lives_ok(
  format(
    'update app.planned_sessions set objective = %L where training_plan_version_id = %L::uuid and scheduled_date = current_date',
    'Adjusted synthetic objective',
    :'draft_id'),
  'sessions in a draft remain editable'
);

select lives_ok(
  format(
    'select app.publish_training_plan_version(%L::uuid, %L::uuid)',
    :'draft_id',
    '11111111-5002-4502-8502-111111111111'),
  'owner can publish a complete draft through the narrow function'
);

select is(
  (select count(*)::integer from app.training_plan_versions
   where owner_id = '11111111-1111-4111-8111-111111111111'
     and status = 'published'),
  1,
  'exactly one plan version remains published for the owner'
);

select is(
  (select count(*)::integer from app.training_plan_versions
   where training_plan_id = '11111111-2000-4200-8200-111111111111'),
  :'version_count_before'::integer + 1,
  'publishing retains both immutable plan versions'
);

select is(
  (select status from app.training_plan_versions
   where id = :'source_id'::uuid),
  'superseded',
  'the previously published version is explicitly superseded'
);

select throws_ok(
  format(
    'update app.planned_sessions set objective = %L where training_plan_version_id = %L::uuid',
    'Mutation that must fail',
    :'draft_id'),
  '23514',
  'Published plan content is immutable.',
  'published session content cannot be edited'
);

select is(
  (select array_agg(b.position order by b.position)
   from app.planned_session_blocks b
   join app.planned_sessions s
     on s.owner_id = b.owner_id and s.id = b.planned_session_id
   where s.training_plan_version_id = :'draft_id'::uuid
     and s.session_type = 'strength_mobility_plyometrics'),
  array[1, 2, 3, 4, 5],
  'published session blocks retain their explicit order'
);

select throws_ok(
  format(
    'update app.planned_session_blocks set instructions = %L where planned_session_id in (select id from app.planned_sessions where training_plan_version_id = %L::uuid)',
    'Mutation that must fail',
    :'draft_id'),
  '23514',
  'Published plan content is immutable.',
  'published block content cannot be edited'
);

select throws_ok(
  format(
    'update app.planned_session_exercises set note = %L where planned_session_block_id in (select b.id from app.planned_session_blocks b join app.planned_sessions s on s.owner_id = b.owner_id and s.id = b.planned_session_id where s.training_plan_version_id = %L::uuid)',
    'Mutation that must fail',
    :'draft_id'),
  '23514',
  'Published plan content is immutable.',
  'published exercise dosage cannot be edited'
);

select throws_ok(
  $$
    select app.clone_training_plan_draft(
      '22222222-2000-4200-8200-222222222222',
      '22222222-2100-4200-8200-222222222222',
      'Cross-owner clone must fail',
      '11111111-5001-4501-8501-111111111111')
  $$,
  'P0002',
  'Training plan not found.',
  'a known plan owned by another athlete remains unavailable'
);

select is(
  (select count(*)::integer from app.audit_events
   where correlation_id in (
     '11111111-5001-4501-8501-111111111111',
     '11111111-5002-4502-8502-111111111111')),
  2,
  'draft and publish operations each write an owner-scoped audit event'
);

select * from finish();
rollback;
