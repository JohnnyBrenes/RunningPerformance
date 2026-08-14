insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values (
  'athlete-files',
  'athlete-files',
  false,
  52428800,
  array['application/octet-stream','application/zip','text/csv','application/json']
)
on conflict (id) do update
set public = excluded.public,
    file_size_limit = excluded.file_size_limit,
    allowed_mime_types = excluded.allowed_mime_types;

create policy athlete_files_owner_select
on storage.objects
for select
to authenticated
using (
  bucket_id = 'athlete-files'
  and (storage.foldername(name))[1] = auth.uid()::text
);

create policy athlete_files_owner_insert
on storage.objects
for insert
to authenticated
with check (
  bucket_id = 'athlete-files'
  and (storage.foldername(name))[1] = auth.uid()::text
);

revoke all on all tables in schema app from anon, authenticated, public;
revoke all on all functions in schema app from anon, authenticated, public;
revoke all on schema app from anon, authenticated, public;
grant usage on schema app to rp_api, rp_worker;

grant execute on function app.current_owner_id(), app.owns(uuid), app.free_tier_quota_state(bigint, integer, integer)
to rp_api, rp_worker;

grant select, insert, update, delete on all tables in schema app to rp_worker;
grant select, insert, update, delete on
  app.athlete_profiles,
  app.athlete_health_contexts,
  app.target_races,
  app.exercises,
  app.sync_clients,
  app.sync_pairing_tokens,
  app.training_plans,
  app.planned_sessions,
  app.planned_session_blocks,
  app.planned_session_exercises,
  app.source_files,
  app.ingestion_runs,
  app.ingestion_items,
  app.activities,
  app.metric_definitions,
  app.activity_metric_values,
  app.activity_source_observations,
  app.activity_field_sources,
  app.fit_processing_attempts,
  app.fit_processing_warnings,
  app.fit_schema_observations,
  app.quarantine_cases,
  app.activity_fit_sessions,
  app.activity_laps,
  app.activity_events,
  app.activity_time_in_zones,
  app.activity_samples,
  app.activity_session_links,
  app.planned_session_outcomes,
  app.session_checkins,
  app.weekly_evaluations,
  app.weekly_evaluation_sessions,
  app.weekly_metric_values,
  app.weekly_metric_evidence,
  app.weekly_decisions,
  app.plan_adjustments,
  app.notes,
  app.export_jobs,
  app.lifecycle_requests
to rp_api;

grant select, insert on
  app.race_goal_versions,
  app.exercise_revisions,
  app.exercise_media,
  app.training_plan_versions,
  app.stored_objects,
  app.audit_events
to rp_api;

grant select on
  app.v_activity_history,
  app.v_activity_srpe,
  app.v_planned_vs_completed,
  app.v_weekly_running,
  app.v_weekly_p1_to_p5_sources,
  app.v_current_race_goals,
  app.v_current_training_plan,
  app.v_current_exercise_revisions
to rp_api, rp_worker;
