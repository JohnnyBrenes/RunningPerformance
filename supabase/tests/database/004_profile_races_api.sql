begin;
set local role postgres;
set local search_path = extensions, public;
grant usage on schema extensions to rp_api;

select plan(8);

delete from app.audit_events
where correlation_id in (
  '11111111-eeee-4111-8111-111111111111',
  '11111111-ffff-4111-8111-111111111111');
delete from app.race_goal_versions
where owner_id = '11111111-1111-4111-8111-111111111111'
  and target_race_id = '11111111-aaaa-4111-8111-111111111111';

set local role rp_api;
select set_config('request.jwt.claim.sub', '11111111-1111-4111-8111-111111111111', true);

select lives_ok(
  $$
    select app.create_race_goal_version(
      '11111111-aaaa-4111-8111-111111111111',
      3600,
      360,
      'low',
      'Synthetic first goal',
      '11111111-eeee-4111-8111-111111111111')
  $$,
  'owner can create the first immutable race goal version'
);

select is(
  (select count(*)::integer from app.race_goal_versions where target_race_id = '11111111-aaaa-4111-8111-111111111111'),
  1,
  'first goal creates one version'
);

select lives_ok(
  $$
    select app.create_race_goal_version(
      '11111111-aaaa-4111-8111-111111111111',
      3540,
      354,
      'medium',
      'Synthetic revised goal',
      '11111111-ffff-4111-8111-111111111111')
  $$,
  'owner can supersede a goal through the narrow versioning function'
);

select is(
  (select count(*)::integer from app.race_goal_versions where target_race_id = '11111111-aaaa-4111-8111-111111111111'),
  2,
  'superseding retains both immutable versions'
);

select is(
  (select count(*)::integer from app.race_goal_versions where target_race_id = '11111111-aaaa-4111-8111-111111111111' and is_current),
  1,
  'exactly one goal remains current'
);

select ok(
  (select version_number = 2 and supersedes_id is not null
   from app.race_goal_versions
   where target_race_id = '11111111-aaaa-4111-8111-111111111111'
     and is_current),
  'current goal points to the superseded version'
);

select throws_ok(
  $$
    select app.create_race_goal_version(
      '22222222-bbbb-4222-8222-222222222222',
      3500,
      350,
      'high',
      'Cross-owner goal must fail',
      '11111111-eeee-4111-8111-111111111111')
  $$,
  'P0002',
  'Race not found.',
  'known race ID owned by another athlete remains unavailable'
);

select is(
  (select count(*)::integer
   from app.audit_events
   where correlation_id in (
     '11111111-eeee-4111-8111-111111111111',
     '11111111-ffff-4111-8111-111111111111')),
  2,
  'each goal version creation writes an owner-scoped audit event'
);

set local role postgres;
delete from app.audit_events
where correlation_id in (
  '11111111-eeee-4111-8111-111111111111',
  '11111111-ffff-4111-8111-111111111111');
delete from app.race_goal_versions
where owner_id = '11111111-1111-4111-8111-111111111111'
  and target_race_id = '11111111-aaaa-4111-8111-111111111111';

select * from finish();
rollback;
