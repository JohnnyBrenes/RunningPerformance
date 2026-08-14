create table app.weekly_evaluations (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null references app.athlete_profiles(owner_id) on delete cascade,
  week_start date not null check (extract(isodow from week_start) = 1),
  week_end date generated always as (week_start + 6) stored,
  format_version text not null,
  plan_version_id uuid,
  cutoff_at timestamptz not null,
  status text not null check (status in ('provisional','final')),
  traffic_light text not null check (traffic_light in ('green','yellow','red')),
  rationale text not null,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (owner_id, id),
  foreign key (owner_id, plan_version_id) references app.training_plan_versions(owner_id, id)
);

create unique index weekly_evaluations_one_final
  on app.weekly_evaluations(owner_id, week_start) where status = 'final';

create table app.weekly_evaluation_sessions (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  weekly_evaluation_id uuid not null,
  planned_session_id uuid,
  activity_id uuid,
  classification text not null,
  execution_status text,
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  check (planned_session_id is not null or activity_id is not null),
  foreign key (owner_id, weekly_evaluation_id) references app.weekly_evaluations(owner_id, id) on delete cascade,
  foreign key (owner_id, planned_session_id) references app.planned_sessions(owner_id, id),
  foreign key (owner_id, activity_id) references app.activities(owner_id, id)
);

create table app.weekly_metric_values (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  weekly_evaluation_id uuid not null,
  metric_code text not null check (metric_code ~ '^(P[1-5]|C[1-4])$'),
  dimension text not null,
  numeric_value numeric,
  boolean_value boolean,
  text_value text,
  unit text,
  status text,
  formula_version text,
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  unique (weekly_evaluation_id, metric_code, dimension),
  check (num_nonnulls(numeric_value, boolean_value, text_value) = 1),
  foreign key (owner_id, weekly_evaluation_id) references app.weekly_evaluations(owner_id, id) on delete cascade
);

create table app.weekly_metric_evidence (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  weekly_metric_value_id uuid not null,
  activity_id uuid,
  planned_session_id uuid,
  session_checkin_id uuid,
  source_observation_id uuid,
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  check (num_nonnulls(activity_id, planned_session_id, session_checkin_id, source_observation_id) = 1),
  foreign key (owner_id, weekly_metric_value_id) references app.weekly_metric_values(owner_id, id) on delete cascade,
  foreign key (owner_id, activity_id) references app.activities(owner_id, id),
  foreign key (owner_id, planned_session_id) references app.planned_sessions(owner_id, id),
  foreign key (owner_id, session_checkin_id) references app.session_checkins(owner_id, id),
  foreign key (owner_id, source_observation_id) references app.activity_source_observations(owner_id, id)
);

create table app.weekly_decisions (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  weekly_evaluation_id uuid not null,
  decision text not null,
  observation text not null,
  evidence text not null,
  historical_comparison text not null,
  interpretation text not null,
  recommendation text not null,
  confirmed_by uuid not null,
  confirmed_at timestamptz not null default now(),
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  foreign key (owner_id, weekly_evaluation_id) references app.weekly_evaluations(owner_id, id)
);

create table app.plan_adjustments (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  weekly_decision_id uuid not null,
  source_plan_version_id uuid not null,
  target_plan_version_id uuid not null,
  target_type text not null,
  adjustment_type text not null,
  before_value jsonb not null,
  after_value jsonb not null,
  rationale text not null,
  review_criterion text not null,
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  foreign key (owner_id, weekly_decision_id) references app.weekly_decisions(owner_id, id),
  foreign key (owner_id, source_plan_version_id) references app.training_plan_versions(owner_id, id),
  foreign key (owner_id, target_plan_version_id) references app.training_plan_versions(owner_id, id)
);

create table app.notes (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null references app.athlete_profiles(owner_id) on delete cascade,
  note_type text not null,
  body text not null,
  target_race_id uuid,
  activity_id uuid,
  planned_session_id uuid,
  weekly_evaluation_id uuid,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (owner_id, id),
  check (num_nonnulls(target_race_id, activity_id, planned_session_id, weekly_evaluation_id) = 1),
  foreign key (owner_id, target_race_id) references app.target_races(owner_id, id),
  foreign key (owner_id, activity_id) references app.activities(owner_id, id),
  foreign key (owner_id, planned_session_id) references app.planned_sessions(owner_id, id),
  foreign key (owner_id, weekly_evaluation_id) references app.weekly_evaluations(owner_id, id)
);

create table app.audit_events (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null references app.athlete_profiles(owner_id) on delete cascade,
  actor_id uuid,
  actor_type text not null,
  action text not null,
  entity_type text not null,
  entity_id uuid,
  correlation_id uuid not null,
  changed_fields text[] not null default '{}'::text[],
  detail jsonb not null default '{}'::jsonb,
  occurred_at timestamptz not null default now(),
  unique (owner_id, id)
);

create table app.export_jobs (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null,
  ingestion_run_id uuid,
  format text not null,
  schema_version text not null,
  status text not null check (status in ('pending','running','completed','failed','expired')),
  stored_object_id uuid,
  requested_at timestamptz not null default now(),
  completed_at timestamptz,
  expires_at timestamptz,
  created_at timestamptz not null default now(),
  unique (owner_id, id),
  foreign key (owner_id, ingestion_run_id) references app.ingestion_runs(owner_id, id),
  foreign key (owner_id, stored_object_id) references app.stored_objects(owner_id, id)
);

create table app.lifecycle_requests (
  id uuid primary key default extensions.gen_random_uuid(),
  owner_id uuid not null references app.athlete_profiles(owner_id) on delete cascade,
  request_type text not null check (request_type in ('archive','delete')),
  scope jsonb not null,
  rationale text not null,
  status text not null check (status in ('requested','approved','running','completed','rejected')),
  approved_by uuid,
  executed_at timestamptz,
  evidence jsonb,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (owner_id, id)
);

create index weekly_evaluations_owner_week on app.weekly_evaluations(owner_id, week_start desc);

do $$
declare table_name text;
begin
  foreach table_name in array array['weekly_evaluations','weekly_evaluation_sessions','weekly_metric_values','weekly_metric_evidence','weekly_decisions','plan_adjustments','notes','export_jobs','lifecycle_requests'] loop
    execute format('alter table app.%I enable row level security', table_name);
    execute format('alter table app.%I force row level security', table_name);
    execute format('create policy owner_select on app.%I for select to rp_api, rp_worker using (app.owns(owner_id))', table_name);
    execute format('create policy owner_insert on app.%I for insert to rp_api, rp_worker with check (app.owns(owner_id))', table_name);
    execute format('create policy owner_update on app.%I for update to rp_api, rp_worker using (app.owns(owner_id)) with check (app.owns(owner_id))', table_name);
    execute format('create policy owner_delete on app.%I for delete to rp_api, rp_worker using (app.owns(owner_id))', table_name);
    execute format('grant select, insert, update, delete on app.%I to rp_api, rp_worker', table_name);
  end loop;

  alter table app.audit_events enable row level security;
  alter table app.audit_events force row level security;
  create policy owner_select on app.audit_events for select to rp_api, rp_worker using (app.owns(owner_id));
  create policy owner_insert on app.audit_events for insert to rp_api, rp_worker with check (app.owns(owner_id));
  grant select, insert on app.audit_events to rp_api, rp_worker;
end
$$;
