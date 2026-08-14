create schema if not exists extensions;
create extension if not exists pgcrypto with schema extensions;
create extension if not exists pgtap with schema extensions;
create schema if not exists app;

do $$
begin
  if not exists (select 1 from pg_roles where rolname = 'rp_api') then
    create role rp_api nologin nosuperuser nocreatedb nocreaterole noinherit nobypassrls;
  end if;
  if not exists (select 1 from pg_roles where rolname = 'rp_worker') then
    create role rp_worker nologin nosuperuser nocreatedb nocreaterole noinherit nobypassrls;
  end if;
end
$$;

grant usage on schema app to rp_api, rp_worker;
grant rp_api, rp_worker to postgres;

create or replace function app.current_owner_id()
returns uuid
language sql
stable
as $$
  select nullif(current_setting('request.jwt.claim.sub', true), '')::uuid
$$;

create or replace function app.owns(candidate_owner_id uuid)
returns boolean
language sql
stable
as $$
  select candidate_owner_id = app.current_owner_id()
$$;

create or replace function app.set_updated_at()
returns trigger
language plpgsql
as $$
begin
  new.updated_at = now();
  return new;
end
$$;

revoke all on function app.current_owner_id() from public;
revoke all on function app.owns(uuid) from public;
grant execute on function app.current_owner_id(), app.owns(uuid) to rp_api, rp_worker;
