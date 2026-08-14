create table app.activity_session_links (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  activity_id uuid not null,
  planned_session_id uuid not null,
  method text not null check (method in ('automatic','manual')),
  criteria jsonb not null default '{}'::jsonb,
  confidence numeric(4,3) check (confidence between 0 and 1),
  status text not null default 'proposed' check (status in ('proposed','confirmed','withdrawn','rejected')),
  supersedes_id uuid,
  actor_id uuid,
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  foreign key (owner_id, activity_id) references app.activities(owner_id, id),
  foreign key (owner_id, planned_session_id) references app.planned_sessions(owner_id, id),
  foreign key (owner_id, supersedes_id) references app.activity_session_links(owner_id, id)
);

create unique index activity_session_links_one_active
  on app.activity_session_links(activity_id) where status in ('proposed','confirmed');

create table app.planned_session_outcomes (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  planned_session_id uuid not null,
  execution_status text not null check (execution_status in ('completed_as_planned','completed_modified','partially_completed','omitted','not_due')),
  modification_reason text,
  confirmed_at timestamptz,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (owner_id, id),
  unique (planned_session_id),
  foreign key (owner_id, planned_session_id) references app.planned_sessions(owner_id, id) on delete cascade
);

create table app.session_checkins (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  activity_id uuid,
  planned_session_id uuid,
  checkin_window text not null check (checkin_window in ('immediate','24h','48h')),
  session_rpe numeric(3,1) check (session_rpe between 1 and 10),
  pain numeric(3,1) check (pain between 0 and 10),
  pain_location text,
  gait_changed boolean,
  fatigue numeric(3,1) check (fatigue between 0 and 10),
  sleep_quality numeric(3,1) check (sleep_quality between 1 and 5),
  perceived_recovery numeric(3,1) check (perceived_recovery between 0 and 10),
  illness_or_symptom text,
  note text,
  recorded_at timestamptz not null default now(),
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  check (activity_id is not null or planned_session_id is not null),
  foreign key (owner_id, activity_id) references app.activities(owner_id, id),
  foreign key (owner_id, planned_session_id) references app.planned_sessions(owner_id, id)
);

do $$
declare table_name text;
begin
  foreach table_name in array array['activity_session_links','planned_session_outcomes','session_checkins'] loop
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
