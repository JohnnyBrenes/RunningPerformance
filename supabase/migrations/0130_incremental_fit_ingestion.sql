alter table app.ingestion_runs
  add column idempotency_key text;

create unique index ingestion_runs_owner_type_idempotency
  on app.ingestion_runs(owner_id, run_type, idempotency_key)
  where idempotency_key is not null;

do $$
declare
  old_constraint text;
begin
  select constraint_name into old_constraint
  from information_schema.table_constraints
  where table_schema = 'app'
    and table_name = 'activity_time_in_zones'
    and constraint_type = 'UNIQUE'
    and constraint_name <> 'activity_time_in_zones_owner_id_id_key'
  order by constraint_name
  limit 1;

  if old_constraint is not null then
    execute format(
      'alter table app.activity_time_in_zones drop constraint %I',
      old_constraint);
  end if;
end
$$;

alter table app.activity_time_in_zones
  add constraint activity_time_in_zones_attempt_source_zone_unique
  unique (activity_id, fit_processing_attempt_id, source_reference, zone_type, zone_index);

update storage.buckets
set allowed_mime_types = array[
  'application/octet-stream',
  'application/zip',
  'text/csv',
  'application/json',
  'application/vnd.ant.fit'
]
where id = 'athlete-files';

alter table app.sync_pairing_tokens
  add column requested_client_name text;

alter table app.sync_pairing_tokens
  add constraint sync_pairing_tokens_client_name_check
  check (
    requested_client_name is null
    or char_length(requested_client_name) between 1 and 80
  );

create or replace function app.claim_fit_ingestion_run(
  p_lease_owner text,
  p_lease_seconds integer)
returns table (
  id uuid,
  owner_id uuid,
  source_file_id uuid,
  correlation_id uuid,
  attempt_count integer,
  run_type text
)
language sql
security definer
set search_path = pg_catalog, app
as $$
  with candidate as (
    select run.id
    from app.ingestion_runs as run
    where run.run_type in ('fit_import', 'fit_reprocess')
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
    run.attempt_count,
    run.run_type;
$$;

revoke all on function app.claim_fit_ingestion_run(text, integer) from public;
grant execute on function app.claim_fit_ingestion_run(text, integer) to rp_worker;

create or replace function app.consume_sync_pairing_token(
  p_token_hash text,
  p_display_name text,
  p_public_token_id text,
  p_secret_hash text,
  p_expires_at timestamptz
)
returns table (owner_id uuid, sync_client_id uuid)
language plpgsql
security definer
set search_path = pg_catalog, app
as $$
declare
  pairing app.sync_pairing_tokens%rowtype;
  created_client_id uuid := extensions.gen_random_uuid();
  selected_display_name text;
begin
  select * into pairing
  from app.sync_pairing_tokens
  where token_hash = p_token_hash
    and used_at is null
    and expires_at > now()
  for update;

  if not found then
    return;
  end if;

  selected_display_name := coalesce(pairing.requested_client_name, trim(p_display_name));

  if p_expires_at > now() + interval '90 days'
     or p_expires_at <= now()
     or char_length(selected_display_name) not between 1 and 80 then
    raise exception 'invalid sync client request';
  end if;

  insert into app.sync_clients (
    id, owner_id, display_name, public_token_id, secret_hash,
    scopes, expires_at)
  values (
    created_client_id, pairing.owner_id, selected_display_name,
    p_public_token_id, p_secret_hash, array['fit.upload']::text[], p_expires_at);

  update app.sync_pairing_tokens
  set used_at = now(), sync_client_id = created_client_id
  where id = pairing.id;

  return query select pairing.owner_id, created_client_id;
end;
$$;

create or replace function app.authenticate_sync_client(
  p_public_token_id text,
  p_secret_hash text
)
returns table (owner_id uuid, sync_client_id uuid, scopes text[])
language sql
security definer
set search_path = pg_catalog, app
as $$
  update app.sync_clients as client
  set last_used_at = now()
  where client.public_token_id = p_public_token_id
    and client.secret_hash = p_secret_hash
    and client.revoked_at is null
    and client.expires_at > now()
    and client.scopes @> array['fit.upload']::text[]
  returning client.owner_id, client.id, client.scopes;
$$;

revoke all on function app.consume_sync_pairing_token(text, text, text, text, timestamptz) from public;
revoke all on function app.authenticate_sync_client(text, text) from public;
grant execute on function app.consume_sync_pairing_token(text, text, text, text, timestamptz) to rp_api;
grant execute on function app.authenticate_sync_client(text, text) to rp_api;

comment on function app.claim_fit_ingestion_run(text, integer) is
  'Atomically claims one eligible FIT import or reprocess job across owners.';
comment on function app.consume_sync_pairing_token(text, text, text, text, timestamptz) is
  'Consumes one short-lived pairing token and returns the owning client identity without exposing a secret.';
comment on function app.authenticate_sync_client(text, text) is
  'Authenticates and accounts for one revocable fit.upload credential.';
