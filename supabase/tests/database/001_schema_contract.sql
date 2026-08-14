begin;
set local role postgres;
set local search_path = extensions, public;

select plan(11);

select is(
  (select count(*)::integer from pg_tables where schemaname = 'app'),
  45,
  'the app schema contains exactly 45 logical tables'
);

select is(
  (select count(*)::integer from pg_views where schemaname = 'app'),
  9,
  'the app schema contains exactly nine planned views'
);

select is(
  (select count(*)::integer from pg_class c join pg_namespace n on n.oid = c.relnamespace
   where n.nspname = 'app' and c.relkind in ('r','p') and c.relrowsecurity),
  45,
  'RLS is enabled on every private table'
);

select is(
  (select count(*)::integer from pg_class c join pg_namespace n on n.oid = c.relnamespace
   where n.nspname = 'app' and c.relkind in ('r','p') and c.relforcerowsecurity),
  45,
  'RLS is forced on every private table'
);

select is(
  (select count(*)::integer from pg_policies where schemaname = 'app' and policyname = 'owner_select'),
  45,
  'every private table has an owner select policy'
);

select is(
  (select rolbypassrls from pg_roles where rolname = 'rp_api'),
  false,
  'API capability role cannot bypass RLS'
);

select is(
  (select rolbypassrls from pg_roles where rolname = 'rp_worker'),
  false,
  'Worker capability role cannot bypass RLS'
);

select is(
  (select public from storage.buckets where id = 'athlete-files'),
  false,
  'athlete-files bucket is private'
);

select is(
  (select count(*)::integer from pg_policies where schemaname = 'storage' and tablename = 'objects' and policyname like 'athlete_files_owner_%'),
  2,
  'Storage has owner-scoped select and insert policies'
);

select is(app.free_tier_quota_state(299::bigint * 1024 * 1024, 300, 400), 'available', 'database capacity is available below warning');
select is(app.free_tier_quota_state(400::bigint * 1024 * 1024, 300, 400), 'blocked', 'database capacity blocks at the preventive threshold');

select * from finish();
rollback;
