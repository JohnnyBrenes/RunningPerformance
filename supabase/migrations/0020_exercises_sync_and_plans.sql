create table app.exercises (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null references app.athlete_profiles(owner_id) on delete cascade,
  slug text not null check (slug ~ '^[a-z0-9]+(?:-[a-z0-9]+)*$'),
  canonical_name text not null,
  movement_pattern text,
  equipment text,
  status text not null default 'active' check (status in ('active','archived')),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (owner_id, id),
  unique (owner_id, slug)
);

create table app.exercise_revisions (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  exercise_id uuid not null,
  version_number integer not null check (version_number > 0),
  display_name text not null,
  brief_description text not null,
  setup text not null,
  execution text not null,
  safety_cues text not null,
  supersedes_id uuid,
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  unique (owner_id, exercise_id, version_number),
  foreign key (owner_id, exercise_id) references app.exercises(owner_id, id) on delete cascade,
  foreign key (owner_id, supersedes_id) references app.exercise_revisions(owner_id, id)
);

create table app.exercise_media (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  exercise_revision_id uuid not null,
  position smallint not null check (position between 1 and 2),
  asset_uri text not null,
  alt_text text not null,
  mime_type text not null,
  source text not null,
  author text,
  license text not null,
  sha256 char(64) check (sha256 is null or sha256 ~ '^[0-9a-f]{64}$'),
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  unique (exercise_revision_id, position),
  foreign key (owner_id, exercise_revision_id) references app.exercise_revisions(owner_id, id) on delete cascade
);

create table app.sync_clients (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null references app.athlete_profiles(owner_id) on delete cascade,
  display_name text not null,
  public_token_id text not null,
  secret_hash text not null,
  scopes text[] not null default array['fit.upload']::text[] check (scopes <@ array['fit.upload']::text[]),
  expires_at timestamptz not null,
  revoked_at timestamptz,
  last_used_at timestamptz,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (owner_id, id),
  unique (public_token_id)
);

create table app.sync_pairing_tokens (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null references app.athlete_profiles(owner_id) on delete cascade,
  token_hash text not null unique,
  expires_at timestamptz not null,
  used_at timestamptz,
  sync_client_id uuid,
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  check (expires_at <= created_at + interval '10 minutes'),
  foreign key (owner_id, sync_client_id) references app.sync_clients(owner_id, id)
);

create table app.training_plans (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null references app.athlete_profiles(owner_id) on delete cascade,
  name text not null,
  purpose text not null,
  target_start date,
  target_end date check (target_end is null or target_start is null or target_end >= target_start),
  status text not null default 'active' check (status in ('active','completed','archived')),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (owner_id, id)
);

create table app.training_plan_versions (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  training_plan_id uuid not null,
  version_number integer not null check (version_number > 0),
  period_start date not null,
  period_end date not null check (period_end >= period_start),
  status text not null default 'draft' check (status in ('draft','published','superseded','archived')),
  rationale text not null,
  supersedes_id uuid,
  published_at timestamptz,
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  unique (owner_id, training_plan_id, version_number),
  foreign key (owner_id, training_plan_id) references app.training_plans(owner_id, id) on delete cascade,
  foreign key (owner_id, supersedes_id) references app.training_plan_versions(owner_id, id)
);

create unique index training_plan_versions_one_published
  on app.training_plan_versions(owner_id) where status = 'published';

create table app.planned_sessions (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  training_plan_version_id uuid not null,
  scheduled_date date not null,
  session_type text not null,
  modality text,
  obligation text not null default 'planned' check (obligation in ('planned','optional')),
  objective text not null,
  distance_m numeric(10,2) check (distance_m >= 0),
  duration_seconds numeric(10,2) check (duration_seconds >= 0),
  target_rpe_min numeric(3,1) check (target_rpe_min between 1 and 10),
  target_rpe_max numeric(3,1) check (target_rpe_max between 1 and 10),
  terrain text,
  warmup text,
  main_set text,
  recoveries text,
  cooldown text,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (owner_id, id),
  foreign key (owner_id, training_plan_version_id) references app.training_plan_versions(owner_id, id) on delete cascade
);

create table app.planned_session_blocks (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  planned_session_id uuid not null,
  position integer not null check (position > 0),
  block_type text not null check (block_type in ('warmup','main','cooldown','circuit','mobility')),
  repeat_count integer not null default 1 check (repeat_count > 0),
  instructions text not null,
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  unique (planned_session_id, position),
  foreign key (owner_id, planned_session_id) references app.planned_sessions(owner_id, id) on delete cascade
);

create table app.planned_session_exercises (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  planned_session_block_id uuid not null,
  exercise_revision_id uuid not null,
  position integer not null check (position > 0),
  sets integer check (sets > 0),
  repetitions_min integer check (repetitions_min > 0),
  repetitions_max integer check (repetitions_max >= repetitions_min),
  duration_seconds numeric(10,2) check (duration_seconds > 0),
  rest_seconds numeric(10,2) check (rest_seconds >= 0),
  load_value numeric(10,2) check (load_value >= 0),
  load_unit text,
  target_rpe numeric(3,1) check (target_rpe between 1 and 10),
  target_rir numeric(3,1) check (target_rir between 0 and 10),
  tempo text,
  side text,
  note text,
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  unique (planned_session_block_id, position),
  check (sets is not null or repetitions_min is not null or duration_seconds is not null),
  foreign key (owner_id, planned_session_block_id) references app.planned_session_blocks(owner_id, id) on delete cascade,
  foreign key (owner_id, exercise_revision_id) references app.exercise_revisions(owner_id, id)
);

create index planned_sessions_owner_date on app.planned_sessions(owner_id, scheduled_date, id);

do $$
declare table_name text;
begin
  foreach table_name in array array['exercises','sync_clients','sync_pairing_tokens','training_plans','planned_sessions','planned_session_blocks','planned_session_exercises'] loop
    execute format('alter table app.%I enable row level security', table_name);
    execute format('alter table app.%I force row level security', table_name);
    execute format('create policy owner_select on app.%I for select to rp_api, rp_worker using (app.owns(owner_id))', table_name);
    execute format('create policy owner_insert on app.%I for insert to rp_api, rp_worker with check (app.owns(owner_id))', table_name);
    execute format('create policy owner_update on app.%I for update to rp_api, rp_worker using (app.owns(owner_id)) with check (app.owns(owner_id))', table_name);
    execute format('create policy owner_delete on app.%I for delete to rp_api, rp_worker using (app.owns(owner_id))', table_name);
    execute format('grant select, insert, update, delete on app.%I to rp_api, rp_worker', table_name);
  end loop;

  foreach table_name in array array['exercise_revisions','exercise_media','training_plan_versions'] loop
    execute format('alter table app.%I enable row level security', table_name);
    execute format('alter table app.%I force row level security', table_name);
    execute format('create policy owner_select on app.%I for select to rp_api, rp_worker using (app.owns(owner_id))', table_name);
    execute format('create policy owner_insert on app.%I for insert to rp_api, rp_worker with check (app.owns(owner_id))', table_name);
    execute format('grant select, insert on app.%I to rp_api, rp_worker', table_name);
  end loop;
end
$$;
