create table app.stored_objects (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null references app.athlete_profiles(owner_id) on delete cascade,
  bucket_id text not null,
  object_path text not null,
  sha256 char(64) not null check (sha256 ~ '^[0-9a-f]{64}$'),
  size_bytes bigint not null check (size_bytes >= 0),
  mime_type text not null,
  retention_class text not null default 'source',
  encryption_context text,
  accepted_at timestamptz,
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  unique (owner_id, sha256),
  unique (bucket_id, object_path)
);

create table app.source_files (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  stored_object_id uuid not null,
  source_kind text not null check (source_kind in ('normalized_csv','fit','export')),
  original_name text not null,
  receipt_method text not null check (receipt_method in ('historical_import','incremental','manual','export')),
  declared_garmin_activity_id bigint,
  status text not null default 'received' check (status in ('received','validated','accepted','quarantined','rejected')),
  received_at timestamptz not null default now(),
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  foreign key (owner_id, stored_object_id) references app.stored_objects(owner_id, id)
);

create table app.ingestion_runs (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null references app.athlete_profiles(owner_id) on delete cascade,
  run_type text not null check (run_type in ('csv_import','fit_import','fit_reprocess','export')),
  status text not null default 'pending' check (status in ('pending','running','succeeded','failed','quarantined','cancelled')),
  tool_version text not null,
  schema_version text not null,
  sdk_version text,
  correlation_id uuid not null default extensions.gen_random_uuid(),
  started_at timestamptz,
  finished_at timestamptz,
  item_count integer not null default 0 check (item_count >= 0),
  success_count integer not null default 0 check (success_count >= 0),
  failure_count integer not null default 0 check (failure_count >= 0),
  lease_owner text,
  lease_until timestamptz,
  heartbeat_at timestamptz,
  attempt_count integer not null default 0 check (attempt_count >= 0),
  next_attempt_at timestamptz,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (owner_id, id)
);

create table app.ingestion_items (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  ingestion_run_id uuid not null,
  ordinal integer not null check (ordinal > 0),
  source_file_id uuid,
  observed_key text,
  target_activity_id uuid,
  status text not null default 'pending' check (status in ('pending','validated','applied','skipped','failed','quarantined')),
  action text,
  error_code text,
  error_message text,
  retryable boolean not null default false,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (owner_id, id),
  unique (ingestion_run_id, ordinal),
  foreign key (owner_id, ingestion_run_id) references app.ingestion_runs(owner_id, id) on delete cascade,
  foreign key (owner_id, source_file_id) references app.source_files(owner_id, id)
);

create table app.activities (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null references app.athlete_profiles(owner_id) on delete cascade,
  provisional_activity_key text,
  garmin_activity_id bigint,
  activity_type text not null,
  activity_category text,
  modality text,
  started_at_local timestamp without time zone not null,
  started_at_utc timestamptz,
  timezone_name text,
  utc_offset_minutes smallint,
  title text,
  distance_m numeric(12,3) check (distance_m >= 0),
  duration_seconds numeric(12,3) check (duration_seconds >= 0),
  moving_seconds numeric(12,3) check (moving_seconds >= 0),
  elapsed_seconds numeric(12,3) check (elapsed_seconds >= 0),
  average_pace_seconds_per_km numeric(10,3) check (average_pace_seconds_per_km >= 0),
  average_speed_mps numeric(10,4) check (average_speed_mps >= 0),
  calories numeric(10,2) check (calories >= 0),
  average_heart_rate_bpm numeric(6,2) check (average_heart_rate_bpm >= 0),
  max_heart_rate_bpm numeric(6,2) check (max_heart_rate_bpm >= 0),
  average_cadence_spm numeric(7,2) check (average_cadence_spm >= 0),
  average_power_w numeric(8,2) check (average_power_w >= 0),
  elevation_gain_m numeric(10,2) check (elevation_gain_m >= 0),
  lap_count integer check (lap_count >= 0),
  validation_status text not null default 'draft' check (validation_status in ('draft','validated','quarantined','published')),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (owner_id, id),
  check (provisional_activity_key is not null or garmin_activity_id is not null)
);

alter table app.ingestion_items
  add foreign key (owner_id, target_activity_id) references app.activities(owner_id, id);

create unique index activities_provisional_key_unique on app.activities(owner_id, provisional_activity_key)
  where provisional_activity_key is not null;
create unique index activities_garmin_id_unique on app.activities(owner_id, garmin_activity_id)
  where garmin_activity_id is not null;
create index activities_owner_started on app.activities(owner_id, started_at_local desc, id);
create index activities_owner_category_started on app.activities(owner_id, activity_category, modality, started_at_local desc);

create table app.metric_definitions (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null references app.athlete_profiles(owner_id) on delete cascade,
  code text not null,
  value_type text not null check (value_type in ('numeric','boolean','text')),
  canonical_unit text,
  category text not null,
  comparison_rule text,
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  unique (owner_id, code)
);

create table app.activity_metric_values (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  activity_id uuid not null,
  metric_definition_id uuid not null,
  numeric_value numeric,
  boolean_value boolean,
  text_value text,
  source_observation_id uuid,
  original_unit text,
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  unique (activity_id, metric_definition_id),
  check (num_nonnulls(numeric_value, boolean_value, text_value) = 1),
  foreign key (owner_id, activity_id) references app.activities(owner_id, id) on delete cascade,
  foreign key (owner_id, metric_definition_id) references app.metric_definitions(owner_id, id)
);

create table app.activity_source_observations (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  activity_id uuid not null,
  source_file_id uuid,
  ingestion_item_id uuid,
  source_class text not null check (source_class in ('normalized_csv_row','fit_session','manual')),
  source_row_number integer,
  observed_keys jsonb not null default '{}'::jsonb,
  summary_payload jsonb not null default '{}'::jsonb,
  linking_result text,
  observed_at timestamptz,
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  foreign key (owner_id, activity_id) references app.activities(owner_id, id) on delete cascade,
  foreign key (owner_id, source_file_id) references app.source_files(owner_id, id),
  foreign key (owner_id, ingestion_item_id) references app.ingestion_items(owner_id, id)
);

alter table app.activity_metric_values
  add foreign key (owner_id, source_observation_id) references app.activity_source_observations(owner_id, id);

create table app.activity_field_sources (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  activity_id uuid not null,
  field_name text not null,
  source_observation_id uuid not null,
  precedence_rule text not null,
  selected_at timestamptz not null default now(),
  unique (owner_id, id),
  unique (activity_id, field_name),
  foreign key (owner_id, activity_id) references app.activities(owner_id, id) on delete cascade,
  foreign key (owner_id, source_observation_id) references app.activity_source_observations(owner_id, id)
);

create index ingestion_runs_owner_started on app.ingestion_runs(owner_id, created_at desc);
create index ingestion_items_run_status on app.ingestion_items(ingestion_run_id, status);
create index source_files_owner_garmin on app.source_files(owner_id, declared_garmin_activity_id);

do $$
declare table_name text;
begin
  foreach table_name in array array['source_files','ingestion_runs','ingestion_items','activities','metric_definitions','activity_metric_values','activity_source_observations','activity_field_sources'] loop
    execute format('alter table app.%I enable row level security', table_name);
    execute format('alter table app.%I force row level security', table_name);
    execute format('create policy owner_select on app.%I for select to rp_api, rp_worker using (app.owns(owner_id))', table_name);
    execute format('create policy owner_insert on app.%I for insert to rp_api, rp_worker with check (app.owns(owner_id))', table_name);
    execute format('create policy owner_update on app.%I for update to rp_api, rp_worker using (app.owns(owner_id)) with check (app.owns(owner_id))', table_name);
    execute format('create policy owner_delete on app.%I for delete to rp_api, rp_worker using (app.owns(owner_id))', table_name);
    execute format('grant select, insert, update, delete on app.%I to rp_api, rp_worker', table_name);
  end loop;

  alter table app.stored_objects enable row level security;
  alter table app.stored_objects force row level security;
  create policy owner_select on app.stored_objects for select to rp_api, rp_worker using (app.owns(owner_id));
  create policy owner_insert on app.stored_objects for insert to rp_api, rp_worker with check (app.owns(owner_id));
  grant select, insert on app.stored_objects to rp_api, rp_worker;
end
$$;
