alter table app.export_jobs
  add column idempotency_key text;

alter table app.export_jobs
  alter column idempotency_key set not null,
  add constraint export_jobs_idempotency_key_not_blank
    check (length(btrim(idempotency_key)) between 8 and 200),
  add constraint export_jobs_supported_format
    check (format = 'json'),
  add constraint export_jobs_supported_schema
    check (schema_version = 'running-performance-export-v1'),
  add constraint export_jobs_completion_consistent
    check (
      (status in ('completed', 'expired')
        and stored_object_id is not null
        and completed_at is not null
        and expires_at is not null
        and expires_at > completed_at)
      or
      (status in ('pending', 'running', 'failed')
        and stored_object_id is null
        and completed_at is null
        and expires_at is null)
    );

create unique index export_jobs_owner_idempotency
  on app.export_jobs(owner_id, idempotency_key);

create index export_jobs_owner_requested
  on app.export_jobs(owner_id, requested_at desc, id desc);

alter table app.lifecycle_requests
  add constraint lifecycle_requests_scope_object
    check (jsonb_typeof(scope) = 'object'),
  add constraint lifecycle_requests_rationale_not_blank
    check (length(btrim(rationale)) between 12 and 2000),
  add constraint lifecycle_requests_approval_consistent
    check (
      (status = 'requested' and approved_by is null and executed_at is null and evidence is null)
      or
      (status in ('approved', 'running') and approved_by = owner_id and executed_at is null)
      or
      (status = 'completed' and approved_by = owner_id and executed_at is not null and evidence is not null)
      or
      (status = 'rejected' and approved_by = owner_id and executed_at is null and evidence is not null)
    );

create index lifecycle_requests_owner_created
  on app.lifecycle_requests(owner_id, created_at desc, id desc);

drop policy if exists owner_update on app.export_jobs;
drop policy if exists owner_delete on app.export_jobs;
drop policy if exists owner_update on app.lifecycle_requests;
drop policy if exists owner_delete on app.lifecycle_requests;
revoke update, delete on app.export_jobs from rp_api;
revoke update, delete on app.lifecycle_requests from rp_api;

create or replace function app.current_quota_usage()
returns table (
  database_bytes bigint,
  storage_bytes bigint,
  activity_sample_count bigint,
  activity_sample_table_bytes bigint
)
language sql
stable
security invoker
set search_path = ''
as $$
  select
    pg_database_size(current_database())::bigint,
    coalesce((
      select sum(stored.size_bytes)::bigint
      from app.stored_objects stored
      where stored.owner_id = app.current_owner_id()
    ), 0),
    coalesce((
      select count(*)::bigint
      from app.activity_samples sample
      join app.activities activity on activity.id = sample.activity_id
      where activity.owner_id = app.current_owner_id()
    ), 0),
    pg_total_relation_size('app.activity_samples'::regclass)::bigint;
$$;

create or replace function app.build_athlete_export()
returns jsonb
language sql
stable
security invoker
set search_path = ''
as $$
  select jsonb_build_object(
    'schemaVersion', 'running-performance-export-v1',
    'generatedAt', now(),
    'athleteId', app.current_owner_id(),
    'data', jsonb_build_object(
      'profile', (
        select to_jsonb(item) - 'owner_id'
        from app.athlete_profiles item
        where item.owner_id = app.current_owner_id()
      ),
      'healthContexts', (
        select coalesce(jsonb_agg(to_jsonb(item) - 'owner_id' order by item.id), '[]'::jsonb)
        from app.athlete_health_contexts item
        where item.owner_id = app.current_owner_id()
      ),
      'targetRaces', (
        select coalesce(jsonb_agg(to_jsonb(item) - 'owner_id' order by item.id), '[]'::jsonb)
        from app.target_races item
        where item.owner_id = app.current_owner_id()
      ),
      'raceGoalVersions', (
        select coalesce(jsonb_agg(to_jsonb(item) - 'owner_id' order by item.id), '[]'::jsonb)
        from app.race_goal_versions item
        where item.owner_id = app.current_owner_id()
      ),
      'activities', (
        select coalesce(jsonb_agg(to_jsonb(item) - 'owner_id' order by item.id), '[]'::jsonb)
        from app.activities item
        where item.owner_id = app.current_owner_id()
      ),
      'activityObservations', (
        select coalesce(jsonb_agg(to_jsonb(item) - 'owner_id' order by item.id), '[]'::jsonb)
        from app.activity_source_observations item
        where item.owner_id = app.current_owner_id()
      ),
      'activityMetricValues', (
        select coalesce(jsonb_agg(to_jsonb(item) - 'owner_id' order by item.id), '[]'::jsonb)
        from app.activity_metric_values item
        where item.owner_id = app.current_owner_id()
      ),
      'activityFieldSources', (
        select coalesce(jsonb_agg(to_jsonb(item) - 'owner_id' order by item.id), '[]'::jsonb)
        from app.activity_field_sources item
        where item.owner_id = app.current_owner_id()
      ),
      'sourceFiles', (
        select coalesce(jsonb_agg((to_jsonb(item) - 'owner_id') - 'stored_object_id' order by item.id), '[]'::jsonb)
        from app.source_files item
        where item.owner_id = app.current_owner_id()
      ),
      'trainingPlans', (
        select coalesce(jsonb_agg(to_jsonb(item) - 'owner_id' order by item.id), '[]'::jsonb)
        from app.training_plans item
        where item.owner_id = app.current_owner_id()
      ),
      'trainingPlanVersions', (
        select coalesce(jsonb_agg(to_jsonb(item) - 'owner_id' order by item.id), '[]'::jsonb)
        from app.training_plan_versions item
        where item.owner_id = app.current_owner_id()
      ),
      'plannedSessions', (
        select coalesce(jsonb_agg(to_jsonb(item) - 'owner_id' order by item.id), '[]'::jsonb)
        from app.planned_sessions item
        where item.owner_id = app.current_owner_id()
      ),
      'sessionActivityLinks', (
        select coalesce(jsonb_agg(to_jsonb(item) - 'owner_id' order by item.id), '[]'::jsonb)
        from app.activity_session_links item
        where item.owner_id = app.current_owner_id()
      ),
      'plannedSessionOutcomes', (
        select coalesce(jsonb_agg(to_jsonb(item) - 'owner_id' order by item.id), '[]'::jsonb)
        from app.planned_session_outcomes item
        where item.owner_id = app.current_owner_id()
      ),
      'sessionCheckins', (
        select coalesce(jsonb_agg(to_jsonb(item) - 'owner_id' order by item.id), '[]'::jsonb)
        from app.session_checkins item
        where item.owner_id = app.current_owner_id()
      ),
      'weeklyEvaluations', (
        select coalesce(jsonb_agg(to_jsonb(item) - 'owner_id' order by item.id), '[]'::jsonb)
        from app.weekly_evaluations item
        where item.owner_id = app.current_owner_id()
      ),
      'weeklyMetricValues', (
        select coalesce(jsonb_agg(to_jsonb(item) - 'owner_id' order by item.id), '[]'::jsonb)
        from app.weekly_metric_values item
        where item.owner_id = app.current_owner_id()
      ),
      'weeklyMetricEvidence', (
        select coalesce(jsonb_agg(to_jsonb(item) - 'owner_id' order by item.id), '[]'::jsonb)
        from app.weekly_metric_evidence item
        where item.owner_id = app.current_owner_id()
      ),
      'weeklyDecisions', (
        select coalesce(jsonb_agg(to_jsonb(item) - 'owner_id' order by item.id), '[]'::jsonb)
        from app.weekly_decisions item
        where item.owner_id = app.current_owner_id()
      ),
      'planAdjustments', (
        select coalesce(jsonb_agg(to_jsonb(item) - 'owner_id' order by item.id), '[]'::jsonb)
        from app.plan_adjustments item
        where item.owner_id = app.current_owner_id()
      ),
      'notes', (
        select coalesce(jsonb_agg(to_jsonb(item) - 'owner_id' order by item.id), '[]'::jsonb)
        from app.notes item
        where item.owner_id = app.current_owner_id()
      ),
      'lifecycleRequests', (
        select coalesce(jsonb_agg(to_jsonb(item) - 'owner_id' order by item.id), '[]'::jsonb)
        from app.lifecycle_requests item
        where item.owner_id = app.current_owner_id()
      )
    ),
    'omissions', jsonb_build_array(
      jsonb_build_object(
        'path', 'activitySamples',
        'reason', 'High-volume FIT samples remain reconstructable from private originals and are omitted from the default consolidated export.',
        'rowCount', (
          select count(*)
          from app.activity_samples sample
          join app.activities activity on activity.id = sample.activity_id
          where activity.owner_id = app.current_owner_id()
        )
      ),
      jsonb_build_object(
        'path', 'privateStoredObjects',
        'reason', 'Private binary CSV/FIT objects and temporary exports require separate authenticated download flows.'
      ),
      jsonb_build_object(
        'path', 'credentialsAndSecrets',
        'reason', 'Authentication, pairing and synchronization secrets are never exported.'
      )
    )
  );
$$;

grant execute on function app.current_quota_usage() to rp_api, rp_worker;
grant execute on function app.build_athlete_export() to rp_api, rp_worker;
