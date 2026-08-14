create table app.athlete_profiles (
  owner_id uuid primary key references auth.users(id) on delete cascade,
  display_name text not null check (length(display_name) between 1 and 120),
  birth_date date,
  height_cm numeric(5,2) check (height_cm > 0),
  weight_kg numeric(5,2) check (weight_kg > 0),
  timezone_name text not null default 'America/Mexico_City',
  locale text not null default 'es-MX',
  unit_system text not null default 'metric' check (unit_system in ('metric')),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create table app.athlete_health_contexts (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null references app.athlete_profiles(owner_id) on delete cascade,
  context_type text not null check (context_type in ('injury_history','discomfort','restriction','other')),
  body_location text,
  started_on date,
  ended_on date check (ended_on is null or started_on is null or ended_on >= started_on),
  status text not null default 'active' check (status in ('active','resolved','monitoring')),
  description text not null,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (owner_id, id)
);

create table app.target_races (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null references app.athlete_profiles(owner_id) on delete cascade,
  name text not null,
  race_date date not null,
  distance_m numeric(10,2) not null check (distance_m > 0),
  location text,
  priority text not null check (priority in ('A','B','C')),
  status text not null default 'planned' check (status in ('planned','completed','cancelled','archived')),
  timezone_name text,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (owner_id, id)
);

create table app.race_goal_versions (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  target_race_id uuid not null,
  version_number integer not null check (version_number > 0),
  goal_time_seconds numeric(10,2) check (goal_time_seconds > 0),
  goal_pace_seconds_per_km numeric(10,2) check (goal_pace_seconds_per_km > 0),
  confidence text check (confidence in ('low','medium','high')),
  rationale text not null,
  supersedes_id uuid,
  is_current boolean not null default true,
  effective_at timestamptz not null default now(),
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  unique (owner_id, target_race_id, version_number),
  foreign key (owner_id, target_race_id) references app.target_races(owner_id, id) on delete cascade,
  foreign key (owner_id, supersedes_id) references app.race_goal_versions(owner_id, id)
);

create unique index race_goal_versions_one_current
  on app.race_goal_versions(owner_id, target_race_id) where is_current;
create index target_races_owner_date on app.target_races(owner_id, race_date, id);

create trigger athlete_profiles_set_updated_at before update on app.athlete_profiles
for each row execute function app.set_updated_at();
create trigger athlete_health_contexts_set_updated_at before update on app.athlete_health_contexts
for each row execute function app.set_updated_at();
create trigger target_races_set_updated_at before update on app.target_races
for each row execute function app.set_updated_at();

do $$
declare table_name text;
begin
  foreach table_name in array array['athlete_profiles','athlete_health_contexts','target_races'] loop
    execute format('alter table app.%I enable row level security', table_name);
    execute format('alter table app.%I force row level security', table_name);
    execute format('create policy owner_select on app.%I for select to rp_api, rp_worker using (app.owns(owner_id))', table_name);
    execute format('create policy owner_insert on app.%I for insert to rp_api, rp_worker with check (app.owns(owner_id))', table_name);
    execute format('create policy owner_update on app.%I for update to rp_api, rp_worker using (app.owns(owner_id)) with check (app.owns(owner_id))', table_name);
    execute format('create policy owner_delete on app.%I for delete to rp_api, rp_worker using (app.owns(owner_id))', table_name);
    execute format('grant select, insert, update, delete on app.%I to rp_api, rp_worker', table_name);
  end loop;

  alter table app.race_goal_versions enable row level security;
  alter table app.race_goal_versions force row level security;
  create policy owner_select on app.race_goal_versions for select to rp_api, rp_worker using (app.owns(owner_id));
  create policy owner_insert on app.race_goal_versions for insert to rp_api, rp_worker with check (app.owns(owner_id));
  grant select, insert on app.race_goal_versions to rp_api, rp_worker;
end
$$;
