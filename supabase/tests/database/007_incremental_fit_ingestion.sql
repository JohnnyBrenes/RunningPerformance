begin;
set local role postgres;
set local search_path = extensions, public;

select plan(14);

select has_column(
  'app', 'ingestion_runs', 'idempotency_key',
  'FIT receipts have a persistent idempotency key'
);

select has_index(
  'app', 'ingestion_runs', 'ingestion_runs_owner_type_idempotency',
  'idempotency is unique per owner and run type'
);

select ok(
  exists (
    select 1
    from pg_catalog.pg_constraint as pc
    join pg_catalog.pg_class as relation on relation.oid = pc.conrelid
    join pg_catalog.pg_namespace as namespace on namespace.oid = relation.relnamespace
    where namespace.nspname = 'app'
      and relation.relname = 'activity_time_in_zones'
      and pc.conname = 'activity_time_in_zones_attempt_source_zone_unique'
      and pc.contype = 'u'
  ),
  'repeated FIT zone messages retain their distinct source references'
);

select ok(
  exists (
    select 1
    from storage.buckets
    where id = 'athlete-files'
      and 'application/vnd.ant.fit' = any(allowed_mime_types)
  ),
  'the private athlete bucket accepts the standard FIT MIME type'
);

select has_column(
  'app', 'sync_pairing_tokens', 'requested_client_name',
  'the authenticated pairing request fixes the client display name'
);

select has_function(
  'app', 'claim_fit_ingestion_run', array['text', 'integer'],
  'FIT jobs are claimed through a narrow queue function'
);

select function_privs_are(
  'app', 'claim_fit_ingestion_run', array['text', 'integer'],
  'rp_worker', array['EXECUTE'],
  'only the worker application role can claim FIT jobs'
);

select function_privs_are(
  'app', 'claim_fit_ingestion_run', array['text', 'integer'],
  'rp_api', array[]::text[],
  'the API application role cannot claim FIT jobs'
);

select function_privs_are(
  'app', 'claim_fit_ingestion_run', array['text', 'integer'],
  'public', array[]::text[],
  'public cannot claim FIT jobs'
);

select has_function(
  'app', 'consume_sync_pairing_token',
  array['text', 'text', 'text', 'text', 'timestamp with time zone'],
  'pairing exchange is atomic and narrowly exposed'
);

select function_privs_are(
  'app', 'consume_sync_pairing_token',
  array['text', 'text', 'text', 'text', 'timestamp with time zone'],
  'rp_api', array['EXECUTE'],
  'the API role can exchange a pairing token'
);

select has_function(
  'app', 'authenticate_sync_client', array['text', 'text'],
  'FIT upload credentials authenticate through one narrow function'
);

select function_privs_are(
  'app', 'authenticate_sync_client', array['text', 'text'],
  'rp_api', array['EXECUTE'],
  'the API role can authenticate a sync client'
);

select function_privs_are(
  'app', 'authenticate_sync_client', array['text', 'text'],
  'public', array[]::text[],
  'public cannot query synchronization credentials directly'
);

select * from finish();
rollback;
