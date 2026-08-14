begin;
set local role postgres;
set local search_path = extensions, public;

select plan(18);

set local role rp_api;
select set_config(
  'request.jwt.claim.sub',
  '11111111-1111-4111-8111-111111111111',
  true);
set local role postgres;

select ok(
  (select database_bytes > 0 from app.current_quota_usage()),
  'quota usage measures the current database without provider estimates');

select is(
  app.build_athlete_export() ->> 'schemaVersion',
  'running-performance-export-v1',
  'the consolidated export has an explicit schema version');

select is(
  app.build_athlete_export() ->> 'athleteId',
  '11111111-1111-4111-8111-111111111111',
  'the export is scoped to the authenticated owner');

select ok(
  not ((app.build_athlete_export() #> '{data,profile}') ? 'owner_id'),
  'internal owner columns are omitted from exported profile data');

select ok(
  exists (
    select 1
    from jsonb_array_elements(app.build_athlete_export() -> 'omissions') item
    where item ->> 'path' = 'activitySamples'
      and (item ->> 'rowCount')::integer = 0),
  'high-volume samples are reviewed explicitly instead of queried by default');

select ok(
  exists (
    select 1
    from jsonb_array_elements(app.build_athlete_export() -> 'omissions') item
    where item ->> 'path' = 'credentialsAndSecrets'),
  'credentials and secrets are explicitly excluded from exports');

insert into app.stored_objects (
  id, owner_id, bucket_id, object_path, sha256, size_bytes,
  mime_type, retention_class, accepted_at)
values
  (
    '11111111-5301-4531-8531-111111111111',
    '11111111-1111-4111-8111-111111111111',
    'athlete-files',
    '11111111-1111-4111-8111-111111111111/export/test/export.json',
    repeat('a', 64), 512, 'application/json', 'temporary_export', now()),
  (
    '11111111-5302-4532-8532-111111111111',
    '11111111-1111-4111-8111-111111111111',
    'athlete-files',
    '11111111-1111-4111-8111-111111111111/export/test/invalid.json',
    repeat('b', 64), 256, 'application/json', 'temporary_export', now());

insert into app.export_jobs (
  id, owner_id, format, schema_version, status, stored_object_id,
  requested_at, completed_at, expires_at, idempotency_key)
values (
  '11111111-5401-4541-8541-111111111111',
  '11111111-1111-4111-8111-111111111111',
  'json', 'running-performance-export-v1', 'completed',
  '11111111-5301-4531-8531-111111111111',
  now(), now(), now() + interval '24 hours', 'synthetic-export-key');

select is(
  (select status from app.export_jobs
   where id = '11111111-5401-4541-8541-111111111111'),
  'completed',
  'a completed export references a private temporary object and expiration');

select throws_ok(
  $$
    insert into app.export_jobs (
      owner_id, format, schema_version, status, stored_object_id,
      requested_at, completed_at, expires_at, idempotency_key)
    values (
      '11111111-1111-4111-8111-111111111111',
      'json', 'running-performance-export-v1', 'completed',
      '11111111-5301-4531-8531-111111111111',
      now(), now(), now() + interval '24 hours', 'synthetic-export-key')
  $$,
  '23505',
  null,
  'export creation is idempotent per owner');

select throws_ok(
  $$
    insert into app.export_jobs (
      owner_id, format, schema_version, status, stored_object_id,
      requested_at, completed_at, expires_at, idempotency_key)
    values (
      '11111111-1111-4111-8111-111111111111',
      'csv', 'running-performance-export-v1', 'completed',
      '11111111-5302-4532-8532-111111111111',
      now(), now(), now() + interval '24 hours', 'invalid-format-key')
  $$,
  '23514',
  null,
  'unsupported export formats are rejected');

select ok(
  (select storage_bytes >= 768 from app.current_quota_usage()),
  'Storage consumption is derived from private object metadata');

insert into app.lifecycle_requests (
  id, owner_id, request_type, scope, rationale, status)
values (
  '11111111-5501-4551-8551-111111111111',
  '11111111-1111-4111-8111-111111111111',
  'archive', '{"type":"all","id":null}',
  'Synthetic explicit archive request.', 'requested');

select is(
  (select status from app.lifecycle_requests
   where id = '11111111-5501-4551-8551-111111111111'),
  'requested',
  'lifecycle requests remain pending human review');

select throws_ok(
  $$
    insert into app.lifecycle_requests (
      owner_id, request_type, scope, rationale, status)
    values (
      '11111111-1111-4111-8111-111111111111',
      'delete', '{"type":"all"}', 'short', 'requested')
  $$,
  '23514',
  null,
  'lifecycle rationale cannot be blank or ambiguous');

select is(
  has_table_privilege('rp_api', 'app.export_jobs', 'UPDATE'),
  false,
  'the API cannot rewrite export audit history');

select is(
  has_table_privilege('rp_api', 'app.export_jobs', 'DELETE'),
  false,
  'the API cannot delete export audit history');

select is(
  has_table_privilege('rp_api', 'app.lifecycle_requests', 'UPDATE'),
  false,
  'the API cannot approve or execute its own lifecycle request');

set local role rp_api;
select count(*)::integer as own_quota_report_count
from app.audit_events
where action = 'free_tier.usage_reported' \gset
set local role postgres;
select is(
  :'own_quota_report_count'::integer,
  1,
  'quota reports are isolated to the authenticated owner');

set local role rp_api;
select set_config(
  'request.jwt.claim.sub',
  '22222222-2222-4222-8222-222222222222',
  true);
select count(*)::integer as other_visible_export_count
from app.export_jobs
where id = '11111111-5401-4541-8541-111111111111' \gset
select app.build_athlete_export() ->> 'athleteId' as other_export_owner \gset
set local role postgres;

select is(
  :'other_visible_export_count'::integer,
  0,
  'RLS hides another owner export job');

select is(
  :'other_export_owner'::text,
  '22222222-2222-4222-8222-222222222222'::text,
  'the same export function cannot cross owner context');

select * from finish();
rollback;
