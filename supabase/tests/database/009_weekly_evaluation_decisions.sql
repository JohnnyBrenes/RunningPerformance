begin;
set local role postgres;
set local search_path = extensions, public;

select plan(16);

delete from app.plan_adjustments
where owner_id = '11111111-1111-4111-8111-111111111111';
delete from app.weekly_decisions
where owner_id = '11111111-1111-4111-8111-111111111111';
delete from app.notes
where owner_id = '11111111-1111-4111-8111-111111111111'
  and weekly_evaluation_id is not null;
delete from app.weekly_evaluations
where owner_id = '11111111-1111-4111-8111-111111111111'
  and week_start = date_trunc('week', current_date)::date;
delete from app.training_plan_versions
where owner_id = '11111111-1111-4111-8111-111111111111'
  and status = 'draft';

set local role rp_api;
select set_config(
  'request.jwt.claim.sub',
  '11111111-1111-4111-8111-111111111111',
  true);
select app.create_weekly_evaluation_snapshot(
  date_trunc('week', current_date)::date,
  'provisional',
  '11111111-5001-4501-8501-111111111111') as evaluation_id \gset
set local role postgres;

select is(
  (select traffic_light from app.weekly_evaluations where id = :'evaluation_id'),
  'yellow',
  'the worst available or missing safety signal produces yellow');

select is(
  (select array_agg(distinct metric_code order by metric_code)
   from app.weekly_metric_values where weekly_evaluation_id = :'evaluation_id'),
  array['P1','P2','P3','P4','P5']::text[],
  'the snapshot freezes all five primary metric families');

select is(
  (select numeric_value from app.weekly_metric_values
   where weekly_evaluation_id = :'evaluation_id'
     and metric_code = 'P4'
     and dimension = 'session:11111111-2201-4201-8201-111111111111'),
  125.00::numeric,
  'P4 counts the two split activities once as one logical sRPE');

select ok(
  exists (
    select 1 from app.weekly_metric_values
    where weekly_evaluation_id = :'evaluation_id'
      and metric_code = 'P2'
      and dimension = 'actual_distance_m:outdoor'
      and status = 'missing'
      and numeric_value is null),
  'missing outdoor data remains NULL with an explicit missing status');

select is(
  (select numeric_value from app.weekly_metric_values
   where weekly_evaluation_id = :'evaluation_id'
     and metric_code = 'P5' and dimension = 'pain'),
  0::numeric,
  'an explicit P5 zero is not confused with missing data');

select is(
  (select count(*)::integer from app.weekly_metric_values metric
   where metric.weekly_evaluation_id = :'evaluation_id'
     and not exists (
       select 1 from app.weekly_metric_evidence evidence
       where evidence.weekly_metric_value_id = metric.id)),
  0,
  'every weekly aggregate has navigable evidence');

select is(
  (select count(*)::integer from app.training_plan_versions
   where owner_id = '11111111-1111-4111-8111-111111111111'
     and status = 'draft'),
  0,
  'an automatic snapshot never creates or publishes a plan version');

insert into app.weekly_decisions (
  id, owner_id, weekly_evaluation_id, decision, observation, evidence,
  historical_comparison, interpretation, recommendation, confirmed_by)
values (
  '11111111-5101-4511-8511-111111111111',
  '11111111-1111-4111-8111-111111111111',
  :'evaluation_id', 'adapt', 'Synthetic observation', 'Synthetic evidence',
  'No comparable synthetic week', 'Review missing response',
  'Create an un-published draft',
  '11111111-1111-4111-8111-111111111111');

select is(
  (select count(*)::integer from app.training_plan_versions
   where owner_id = '11111111-1111-4111-8111-111111111111'
     and status = 'draft'),
  0,
  'a confirmed narrative alone still does not change the plan');

set local role rp_api;
select set_config(
  'request.jwt.claim.sub',
  '11111111-1111-4111-8111-111111111111',
  true);
select (app.clone_training_plan_draft(
  '11111111-2000-4200-8200-111111111111',
  '11111111-2100-4200-8200-111111111111',
  'Synthetic human-confirmed APP-011 adjustment',
  '11111111-5201-4521-8521-111111111111')).id as target_version_id \gset
set local role postgres;

insert into app.plan_adjustments (
  owner_id, weekly_decision_id, source_plan_version_id,
  target_plan_version_id, target_type, adjustment_type,
  before_value, after_value, rationale, review_criterion)
values (
  '11111111-1111-4111-8111-111111111111',
  '11111111-5101-4511-8511-111111111111',
  '11111111-2100-4200-8200-111111111111', :'target_version_id',
  'planned_session', 'objective',
  '{"objective":"Original synthetic objective"}',
  '{"objective":"Adjusted synthetic objective"}',
  'Synthetic human-confirmed rationale',
  'Review after complete 24-to-48-hour response');

select ok(
  (select status = 'published' from app.training_plan_versions
   where id = '11111111-2100-4200-8200-111111111111')
  and
  (select status = 'draft' from app.training_plan_versions
   where id = :'target_version_id'),
  'a human-confirmed adjustment preserves the publication and creates a new draft');

set local role rp_api;
select set_config(
  'request.jwt.claim.sub',
  '22222222-2222-4222-8222-222222222222',
  true);
select (
  not exists (
    select 1 from app.weekly_evaluations where id = :'evaluation_id')
)::boolean as other_owner_hidden \gset
set local role postgres;

select ok(:'other_owner_hidden'::boolean, 'another owner cannot read the snapshot');

set local role rp_api;
select set_config(
  'request.jwt.claim.sub',
  '11111111-1111-4111-8111-111111111111',
  true);
set local role postgres;

select ok(
  not has_table_privilege('rp_api', 'app.weekly_evaluations', 'UPDATE'),
  'snapshots cannot be updated by the API role');

set local role rp_api;
select set_config(
  'request.jwt.claim.sub',
  '11111111-1111-4111-8111-111111111111',
  true);
select app.create_weekly_evaluation_snapshot(
  date_trunc('week', current_date)::date,
  'final',
  '11111111-5002-4502-8502-111111111111') as final_evaluation_id \gset
set local role postgres;

select throws_ok(
  $$
    insert into app.weekly_evaluations (
      owner_id, week_start, format_version, plan_version_id, cutoff_at,
      status, traffic_light, rationale)
    select owner_id, week_start, format_version, plan_version_id, now(),
      'final', traffic_light, rationale
    from app.weekly_evaluations
    where owner_id = '11111111-1111-4111-8111-111111111111'
      and week_start = date_trunc('week', current_date)::date
      and status = 'final'
    limit 1
  $$,
  '23505',
  null,
  'only one final snapshot can exist for a week');

select is(
  (select count(*)::integer from app.weekly_metric_values
   where weekly_evaluation_id = :'evaluation_id'
     and metric_code = 'P5'),
  7,
  'P5 remains seven independent components without a composite score');

select ok(
  exists (
    select 1 from app.weekly_metric_values
    where weekly_evaluation_id = :'evaluation_id'
      and metric_code = 'P3'
      and dimension = 'outdoor_long_run_observation'
      and status = 'missing'
      and text_value is null),
  'an absent long run is explicit ND rather than an invented observation');

select throws_ok(
  $$
    insert into app.plan_adjustments (
      owner_id, weekly_decision_id, source_plan_version_id,
      target_plan_version_id, target_type, adjustment_type,
      before_value, after_value, rationale, review_criterion)
    values (
      '11111111-1111-4111-8111-111111111111',
      extensions.gen_random_uuid(),
      '11111111-2100-4200-8200-111111111111',
      '11111111-2100-4200-8200-111111111111',
      'planned_session', 'objective', '{}', '{}', 'Synthetic', 'Review')
  $$,
  '23514',
  null::text,
  'an adjustment cannot point source and target to the same plan version');

select is(
  (select count(*)::integer from app.audit_events
   where entity_id in (:'evaluation_id', :'final_evaluation_id')
     and action = 'weekly_evaluation.snapshot_created'),
  2,
  'snapshot creation is append-only audited');

select * from finish();
rollback;
