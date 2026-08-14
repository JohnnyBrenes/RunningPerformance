create or replace function app.claim_csv_ingestion_run(
  p_lease_owner text,
  p_lease_seconds integer)
returns table (
  id uuid,
  owner_id uuid,
  source_file_id uuid,
  correlation_id uuid,
  attempt_count integer)
language sql
security definer
set search_path = pg_catalog, app
as $$
  with candidate as (
    select run.id
    from app.ingestion_runs as run
    where run.run_type = 'csv_import'
      and (
        (
          run.status = 'pending'
          and coalesce(run.next_attempt_at, '-infinity'::timestamptz) <= now()
        )
        or (run.status = 'running' and run.lease_until < now())
      )
    order by run.created_at, run.id
    for update skip locked
    limit 1
  )
  update app.ingestion_runs as run
  set status = 'running',
      lease_owner = p_lease_owner,
      lease_until = now() + make_interval(secs => p_lease_seconds),
      heartbeat_at = now(),
      attempt_count = run.attempt_count + 1,
      started_at = coalesce(run.started_at, now()),
      next_attempt_at = null
  from candidate
  where run.id = candidate.id
  returning
    run.id,
    run.owner_id,
    run.source_file_id,
    run.correlation_id,
    run.attempt_count;
$$;

revoke all on function app.claim_csv_ingestion_run(text, integer) from public;
grant execute on function app.claim_csv_ingestion_run(text, integer) to rp_worker;

comment on function app.claim_csv_ingestion_run(text, integer) is
  'Atomically claims one eligible CSV job across owners; callable only by the worker role.';
