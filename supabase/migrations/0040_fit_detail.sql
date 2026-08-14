create table app.fit_processing_attempts (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  source_file_id uuid not null,
  ingestion_run_id uuid not null,
  processor_version text not null,
  sdk_version text not null,
  schema_version text not null,
  signature_valid boolean not null,
  declared_size_valid boolean not null,
  crc_valid boolean not null,
  full_read_valid boolean not null,
  sha256 char(64) not null check (sha256 ~ '^[0-9a-f]{64}$'),
  message_count integer not null default 0 check (message_count >= 0),
  record_count integer not null default 0 check (record_count >= 0),
  status text not null check (status in ('validated','failed','quarantined')),
  is_current boolean not null default false,
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  foreign key (owner_id, source_file_id) references app.source_files(owner_id, id),
  foreign key (owner_id, ingestion_run_id) references app.ingestion_runs(owner_id, id)
);

create unique index fit_processing_attempts_one_current
  on app.fit_processing_attempts(owner_id, source_file_id) where is_current;

create table app.fit_processing_warnings (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  fit_processing_attempt_id uuid not null,
  code text not null,
  message text not null,
  occurrence_count integer not null default 1 check (occurrence_count > 0),
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  foreign key (owner_id, fit_processing_attempt_id) references app.fit_processing_attempts(owner_id, id) on delete cascade
);

create table app.fit_schema_observations (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  fit_processing_attempt_id uuid not null,
  message_name text,
  global_message_number integer,
  field_name text,
  field_number integer,
  base_type text,
  unit text,
  profile_version text,
  is_developer_field boolean not null default false,
  valid_count integer not null default 0 check (valid_count >= 0),
  invalid_count integer not null default 0 check (invalid_count >= 0),
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  foreign key (owner_id, fit_processing_attempt_id) references app.fit_processing_attempts(owner_id, id) on delete cascade
);

create table app.quarantine_cases (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  source_file_id uuid,
  ingestion_item_id uuid,
  reason_code text not null,
  details jsonb not null default '{}'::jsonb,
  status text not null default 'open' check (status in ('open','resolved','rejected')),
  resolution text,
  resolved_by uuid,
  resolved_at timestamptz,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (owner_id, id),
  foreign key (owner_id, source_file_id) references app.source_files(owner_id, id),
  foreign key (owner_id, ingestion_item_id) references app.ingestion_items(owner_id, id)
);

create table app.activity_fit_sessions (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  activity_id uuid not null,
  fit_processing_attempt_id uuid not null,
  sequence integer not null check (sequence >= 0),
  sport text,
  sub_sport text,
  started_at_utc timestamptz,
  duration_seconds numeric(12,3) check (duration_seconds >= 0),
  distance_m numeric(12,3) check (distance_m >= 0),
  summary jsonb not null default '{}'::jsonb,
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  unique (fit_processing_attempt_id, sequence),
  foreign key (owner_id, activity_id) references app.activities(owner_id, id) on delete cascade,
  foreign key (owner_id, fit_processing_attempt_id) references app.fit_processing_attempts(owner_id, id) on delete cascade
);

create table app.activity_laps (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  activity_id uuid not null,
  activity_fit_session_id uuid,
  fit_processing_attempt_id uuid not null,
  lap_index integer not null check (lap_index >= 0),
  started_at_utc timestamptz,
  ended_at_utc timestamptz,
  duration_seconds numeric(12,3) check (duration_seconds >= 0),
  distance_m numeric(12,3) check (distance_m >= 0),
  summary jsonb not null default '{}'::jsonb,
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  unique (activity_id, fit_processing_attempt_id, lap_index),
  foreign key (owner_id, activity_id) references app.activities(owner_id, id) on delete cascade,
  foreign key (owner_id, activity_fit_session_id) references app.activity_fit_sessions(owner_id, id),
  foreign key (owner_id, fit_processing_attempt_id) references app.fit_processing_attempts(owner_id, id)
);

create table app.activity_events (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  activity_id uuid not null,
  activity_fit_session_id uuid,
  fit_processing_attempt_id uuid not null,
  event_index integer not null check (event_index >= 0),
  recorded_at_utc timestamptz,
  event_name text,
  event_type text,
  event_group text,
  event_data text,
  additional_fields jsonb not null default '{}'::jsonb,
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  unique (activity_id, fit_processing_attempt_id, event_index),
  foreign key (owner_id, activity_id) references app.activities(owner_id, id) on delete cascade,
  foreign key (owner_id, activity_fit_session_id) references app.activity_fit_sessions(owner_id, id),
  foreign key (owner_id, fit_processing_attempt_id) references app.fit_processing_attempts(owner_id, id)
);

create table app.activity_time_in_zones (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  activity_id uuid not null,
  activity_fit_session_id uuid,
  fit_processing_attempt_id uuid not null,
  zone_type text not null,
  zone_index integer not null check (zone_index >= 0),
  lower_bound numeric,
  upper_bound numeric,
  duration_seconds numeric(12,3) not null check (duration_seconds >= 0),
  source_reference text,
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  unique (activity_id, fit_processing_attempt_id, zone_type, zone_index),
  foreign key (owner_id, activity_id) references app.activities(owner_id, id) on delete cascade,
  foreign key (owner_id, activity_fit_session_id) references app.activity_fit_sessions(owner_id, id),
  foreign key (owner_id, fit_processing_attempt_id) references app.fit_processing_attempts(owner_id, id)
);

create table app.activity_samples (
  owner_id uuid not null,
  activity_id uuid not null,
  fit_processing_attempt_id uuid not null,
  sample_index integer not null check (sample_index >= 0),
  recorded_at_utc timestamptz,
  distance_m numeric(12,3) check (distance_m >= 0),
  latitude_degrees numeric(10,7),
  longitude_degrees numeric(10,7),
  altitude_m numeric(10,3),
  speed_mps numeric(10,4) check (speed_mps >= 0),
  heart_rate_bpm numeric(6,2) check (heart_rate_bpm >= 0),
  cadence_spm numeric(7,2) check (cadence_spm >= 0),
  power_w numeric(8,2) check (power_w >= 0),
  temperature_c numeric(6,2),
  additional_fields jsonb not null default '{}'::jsonb,
  created_at timestamptz not null default now(),
  primary key (activity_id, sample_index),
  unique (owner_id, activity_id, sample_index),
  foreign key (owner_id, activity_id) references app.activities(owner_id, id) on delete cascade,
  foreign key (owner_id, fit_processing_attempt_id) references app.fit_processing_attempts(owner_id, id)
);

create index activity_samples_activity_time on app.activity_samples(activity_id, recorded_at_utc);
create index activity_laps_activity_index on app.activity_laps(activity_id, lap_index);
create index activity_events_activity_index on app.activity_events(activity_id, event_index);

do $$
declare table_name text;
begin
  foreach table_name in array array['fit_processing_attempts','fit_processing_warnings','fit_schema_observations','quarantine_cases','activity_fit_sessions','activity_laps','activity_events','activity_time_in_zones','activity_samples'] loop
    execute format('alter table app.%I enable row level security', table_name);
    execute format('alter table app.%I force row level security', table_name);
    execute format('create policy owner_select on app.%I for select to rp_api, rp_worker using (app.owns(owner_id))', table_name);
    execute format('create policy owner_insert on app.%I for insert to rp_api, rp_worker with check (app.owns(owner_id))', table_name);
    execute format('create policy owner_update on app.%I for update to rp_api, rp_worker using (app.owns(owner_id)) with check (app.owns(owner_id))', table_name);
    execute format('create policy owner_delete on app.%I for delete to rp_api, rp_worker using (app.owns(owner_id))', table_name);
    execute format('grant select, insert, update, delete on app.%I to rp_api, rp_worker', table_name);
  end loop;
end
$$;
