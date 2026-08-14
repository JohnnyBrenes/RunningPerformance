begin;
set local role postgres;
set local search_path = extensions, public;

select plan(10);

select has_column(
  'app',
  'ingestion_runs',
  'source_file_id',
  'ingestion runs identify their immutable private source file'
);

select fk_ok(
  'app',
  'ingestion_runs',
  array['owner_id', 'source_file_id'],
  'app',
  'source_files',
  array['owner_id', 'id'],
  'queued source ownership is enforced by a composite foreign key'
);

select has_index(
  'app',
  'ingestion_runs',
  'ingestion_runs_claimable',
  'pending and expired jobs have a claim index'
);

select trigger_is(
  'app',
  'ingestion_runs',
  'ingestion_runs_set_updated_at',
  'app',
  'set_updated_at',
  'ingestion run progress refreshes updated_at'
);

select trigger_is(
  'app',
  'ingestion_items',
  'ingestion_items_set_updated_at',
  'app',
  'set_updated_at',
  'ingestion item progress refreshes updated_at'
);

select trigger_is(
  'app',
  'activities',
  'activities_set_updated_at',
  'app',
  'set_updated_at',
  'activity reconciliation refreshes updated_at'
);

select has_function(
  'app',
  'claim_csv_ingestion_run',
  array['text', 'integer'],
  'worker queue claiming is exposed through a narrow database function'
);

select function_privs_are(
  'app',
  'claim_csv_ingestion_run',
  array['text', 'integer'],
  'rp_worker',
  array['EXECUTE'],
  'the worker role can execute the queue claim function'
);

select function_privs_are(
  'app',
  'claim_csv_ingestion_run',
  array['text', 'integer'],
  'rp_api',
  array[]::text[],
  'the API role cannot claim cross-owner jobs'
);

select function_privs_are(
  'app',
  'claim_csv_ingestion_run',
  array['text', 'integer'],
  'public',
  array[]::text[],
  'public cannot claim cross-owner jobs'
);

select * from finish();
rollback;
