begin;
set local role postgres;
set local search_path = extensions, public;

select plan(8);

select cmp_ok(
  (select count(*)::bigint
   from pg_constraint c
   join pg_class t on t.oid = c.conrelid
   join pg_namespace n on n.oid = t.relnamespace
   where n.nspname = 'app'
     and c.contype = 'f'
     and array_length(c.conkey, 1) = 2),
  '>=',
  60::bigint,
  'owner-scoped parent relationships use composite foreign keys'
);

select is(
  (select count(*)::integer
   from pg_indexes
   where schemaname = 'app'
     and indexdef ilike '% where %'),
  12,
  'all twelve planned partial indexes exist'
);

select ok(
  exists (
    select 1 from pg_indexes
    where schemaname = 'app'
      and indexname = 'activities_provisional_key_unique'
      and indexdef ilike '%provisional_activity_key is not null%'
  ),
  'provisional activity keys are unique only when present'
);

select ok(
  exists (
    select 1 from pg_indexes
    where schemaname = 'app'
      and indexname = 'activities_garmin_id_unique'
      and indexdef ilike '%garmin_activity_id is not null%'
  ),
  'Garmin activity IDs are unique only when present'
);

select is(
  (select count(*)::integer
   from pg_policies
   where schemaname = 'app'
     and tablename in (
       'race_goal_versions',
       'exercise_revisions',
       'training_plan_versions',
       'stored_objects',
       'audit_events'
     )
     and cmd in ('UPDATE', 'DELETE')),
  0,
  'immutable tables expose no update or delete policy'
);

select throws_ok(
  $$
    insert into app.race_goal_versions (
      owner_id,
      target_race_id,
      version_number,
      rationale,
      is_current
    ) values (
      '11111111-1111-4111-8111-111111111111',
      '22222222-bbbb-4222-8222-222222222222',
      991,
      'Cross-owner relationship must fail',
      false
    )
  $$,
  '23503',
  'insert or update on table "race_goal_versions" violates foreign key constraint "race_goal_versions_owner_id_target_race_id_fkey"',
  'composite foreign keys reject a known parent ID owned by another athlete'
);

insert into app.activities (
  owner_id,
  provisional_activity_key,
  activity_type,
  started_at_local
) values (
  '11111111-1111-4111-8111-111111111111',
  'app005-partial-key-contract',
  'running',
  '2027-01-01 06:00:00'
);

select throws_ok(
  $$
    insert into app.activities (
      owner_id,
      provisional_activity_key,
      activity_type,
      started_at_local
    ) values (
      '11111111-1111-4111-8111-111111111111',
      'app005-partial-key-contract',
      'running',
      '2027-01-01 07:00:00'
    )
  $$,
  '23505',
  'duplicate key value violates unique constraint "activities_provisional_key_unique"',
  'a duplicate non-null provisional activity key is rejected'
);

insert into app.race_goal_versions (
  id,
  owner_id,
  target_race_id,
  version_number,
  rationale,
  is_current
) values (
  '11111111-cccc-4111-8111-111111111111',
  '11111111-1111-4111-8111-111111111111',
  '11111111-aaaa-4111-8111-111111111111',
  991,
  'Immutable synthetic goal',
  false
);

set local role rp_worker;
select set_config('request.jwt.claim.sub', '11111111-1111-4111-8111-111111111111', true);
with changed as (
  update app.race_goal_versions
  set rationale = 'Mutation that must not happen'
  where id = '11111111-cccc-4111-8111-111111111111'
  returning 1
)
select (count(*) = 0)::boolean as immutable_update_blocked
from changed
\gset
set local role postgres;

select ok(:'immutable_update_blocked'::boolean, 'Worker cannot update an immutable version through RLS');

select * from finish();
rollback;
