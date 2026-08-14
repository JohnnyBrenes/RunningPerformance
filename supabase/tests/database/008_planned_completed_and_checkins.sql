begin;
set local role postgres;
set local search_path = extensions, public;

select plan(14);

-- The seed demonstrates the same flow for E2E. Isolate this transactional
-- fixture so its expected counts remain exact and rollback restores the seed.
delete from app.plan_adjustments
where owner_id = '11111111-1111-4111-8111-111111111111';
delete from app.weekly_decisions
where owner_id = '11111111-1111-4111-8111-111111111111';
delete from app.notes
where owner_id = '11111111-1111-4111-8111-111111111111'
  and weekly_evaluation_id is not null;
delete from app.weekly_evaluations
where owner_id = '11111111-1111-4111-8111-111111111111';
delete from app.session_checkins
where planned_session_id = '11111111-2201-4201-8201-111111111111';
delete from app.planned_session_outcomes
where planned_session_id = '11111111-2201-4201-8201-111111111111';
delete from app.activity_session_links
where planned_session_id = '11111111-2201-4201-8201-111111111111';

insert into app.activities (
  id, owner_id, provisional_activity_key, activity_type, activity_category,
  modality, started_at_local, title, distance_m, duration_seconds,
  validation_status)
values
  (
    '11111111-4001-4401-8401-111111111111',
    '11111111-1111-4111-8111-111111111111',
    'app010-split-a', 'running', 'running', 'treadmill',
    current_date + time '06:00', 'Synthetic split A', 1000, 600, 'published'),
  (
    '11111111-4002-4402-8402-111111111111',
    '11111111-1111-4111-8111-111111111111',
    'app010-split-b', 'running', 'running', 'treadmill',
    current_date + time '06:12', 'Synthetic split B', 1500, 900, 'published'),
  (
    '11111111-4003-4403-8403-111111111111',
    '11111111-1111-4111-8111-111111111111',
    'app010-moved', 'strength_training', 'strength', 'indoor',
    current_date + time '18:00', 'Synthetic moved activity', null, 1200, 'published');

insert into app.activity_session_links (
  id, owner_id, activity_id, planned_session_id, method, criteria,
  confidence, status, actor_id)
values
  (
    '11111111-4101-4411-8411-111111111111',
    '11111111-1111-4111-8111-111111111111',
    '11111111-4001-4401-8401-111111111111',
    '11111111-2201-4201-8201-111111111111',
    'manual', '{"source":"athlete_selection"}', null, 'confirmed',
    '11111111-1111-4111-8111-111111111111'),
  (
    '11111111-4102-4412-8412-111111111111',
    '11111111-1111-4111-8111-111111111111',
    '11111111-4002-4402-8402-111111111111',
    '11111111-2201-4201-8201-111111111111',
    'manual', '{"source":"athlete_selection"}', null, 'confirmed',
    '11111111-1111-4111-8111-111111111111'),
  (
    '11111111-4103-4413-8413-111111111111',
    '11111111-1111-4111-8111-111111111111',
    '11111111-4003-4403-8403-111111111111',
    '11111111-2201-4201-8201-111111111111',
    'automatic', '{"ruleVersion":"APP-010-v1"}', 0.9, 'proposed', null);

insert into app.planned_session_outcomes (
  owner_id, planned_session_id, execution_status, modification_reason,
  confirmed_at)
values (
  '11111111-1111-4111-8111-111111111111',
  '11111111-2201-4201-8201-111111111111',
  'completed_modified', 'The synthetic session was split in two files.', now());

insert into app.session_checkins (
  owner_id, planned_session_id, checkin_window, session_rpe, pain,
  gait_changed, fatigue, sleep_quality, perceived_recovery,
  has_illness_or_symptom, recorded_at)
values (
  '11111111-1111-4111-8111-111111111111',
  '11111111-2201-4201-8201-111111111111',
  'immediate', 5, 0, false, 4, 5, 7, false, now());

insert into app.session_checkins (
  owner_id, planned_session_id, checkin_window, recovery_response,
  pain, gait_changed, has_illness_or_symptom, recorded_at)
values (
  '11111111-1111-4111-8111-111111111111',
  '11111111-2201-4201-8201-111111111111',
  '24h', 'normal', 0, false, false, now());

select is(
  (select activity_count from app.v_logical_session_srpe
   where planned_session_id = '11111111-2201-4201-8201-111111111111'),
  2,
  'two confirmed activities form one logical session');

select is(
  (select duration_seconds from app.v_logical_session_srpe
   where planned_session_id = '11111111-2201-4201-8201-111111111111'),
  1500::numeric,
  'logical duration sums both confirmed activities');

select is(
  (select distance_m from app.v_logical_session_srpe
   where planned_session_id = '11111111-2201-4201-8201-111111111111'),
  2500::numeric,
  'logical distance sums both confirmed activities');

select is(
  (select srpe_load from app.v_logical_session_srpe
   where planned_session_id = '11111111-2201-4201-8201-111111111111'),
  125.00::numeric,
  'sRPE uses total logical duration once');

select is(
  (select count(*)::integer from app.v_planned_vs_completed
   where planned_session_id = '11111111-2201-4201-8201-111111111111'),
  1,
  'planned versus completed exposes one row per planned session');

select is(
  (select execution_status from app.v_planned_vs_completed
   where planned_session_id = '11111111-2201-4201-8201-111111111111'),
  'completed_modified',
  'the TRN-003 execution status remains explicit');

select throws_ok(
  $$
    insert into app.planned_session_outcomes (
      owner_id, planned_session_id, execution_status)
    values (
      '11111111-1111-4111-8111-111111111111',
      '11111111-2202-4202-8202-111111111111',
      'valid_substitution')
  $$,
  '23514',
  null,
  'modified, substituted and omitted outcomes require a reason');

select throws_ok(
  $$
    insert into app.session_checkins (
      owner_id, planned_session_id, checkin_window, session_rpe)
    values (
      '11111111-1111-4111-8111-111111111111',
      '11111111-2202-4202-8202-111111111111',
      '24h', 5)
  $$,
  '23514',
  null,
  'RPE is accepted only in the immediate window');

select throws_ok(
  $$
    insert into app.session_checkins (
      owner_id, planned_session_id, checkin_window, pain)
    values (
      '11111111-1111-4111-8111-111111111111',
      '11111111-2201-4201-8201-111111111111',
      'immediate', 1)
  $$,
  '23505',
  null,
  'a logical session has only one check-in per window');

select throws_ok(
  $$
    insert into app.activity_session_links (
      owner_id, activity_id, planned_session_id, method, status)
    values (
      '11111111-1111-4111-8111-111111111111',
      '11111111-4003-4403-8403-111111111111',
      '11111111-2202-4202-8202-111111111111',
      'manual', 'confirmed')
  $$,
  '23505',
  null,
  'an activity has at most one active planned-session link');

update app.activity_session_links
set status = 'withdrawn'
where id = '11111111-4103-4413-8413-111111111111';

insert into app.activity_session_links (
  owner_id, activity_id, planned_session_id, method, criteria,
  status, supersedes_id, actor_id)
values (
  '11111111-1111-4111-8111-111111111111',
  '11111111-4003-4403-8403-111111111111',
  '11111111-2202-4202-8202-111111111111',
  'manual', '{"source":"athlete_selection"}', 'confirmed',
  '11111111-4103-4413-8413-111111111111',
  '11111111-1111-4111-8111-111111111111');

select is(
  (select count(*)::integer from app.activity_session_links
   where activity_id = '11111111-4003-4403-8403-111111111111'),
  2,
  'changing a link preserves both versions');

select is(
  (select count(*)::integer from app.activity_session_links
   where activity_id = '11111111-4003-4403-8403-111111111111'
     and status in ('proposed', 'confirmed')),
  1,
  'changing a link leaves exactly one active version');

set local role rp_api;
select set_config(
  'request.jwt.claim.sub',
  '22222222-2222-4222-8222-222222222222',
  true);
select (
  not exists (
    select 1 from app.v_logical_session_srpe
    where planned_session_id = '11111111-2201-4201-8201-111111111111')
)::boolean as other_owner_hidden \gset
set local role postgres;

select ok(:'other_owner_hidden'::boolean, 'logical session load obeys owner RLS');

select is(
  (select recovery_response from app.session_checkins
   where planned_session_id = '11111111-2201-4201-8201-111111111111'
     and checkin_window = '24h'),
  'normal',
  'the 24-hour recovery response is stored explicitly');

select * from finish();
rollback;
