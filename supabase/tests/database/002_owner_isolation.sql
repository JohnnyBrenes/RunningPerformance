begin;
set local role postgres;
set local search_path = extensions, public;

select plan(9);

delete from app.target_races
where id = '11111111-dddd-4111-8111-111111111111';

set local role rp_api;
select set_config('request.jwt.claim.sub', '11111111-1111-4111-8111-111111111111', true);
select (count(*) = 1)::boolean as owner_a_profile_count_ok,
       (min(display_name) = 'Synthetic Athlete A')::boolean as owner_a_profile_value_ok
from app.athlete_profiles \gset
select (
  exists(select 1 from app.target_races where id = '11111111-aaaa-4111-8111-111111111111')
  and not exists(select 1 from app.target_races where id = '22222222-bbbb-4222-8222-222222222222')
)::boolean as owner_a_race_count_ok \gset
set local role postgres;

select ok(:'owner_a_profile_count_ok'::boolean, 'owner A sees only its profile');
select ok(:'owner_a_profile_value_ok'::boolean, 'owner A sees its own values');
select ok(:'owner_a_race_count_ok'::boolean, 'owner A sees only its race');

set local role rp_api;
select set_config('request.jwt.claim.sub', '11111111-1111-4111-8111-111111111111', true);
insert into app.target_races (id, owner_id, name, race_date, distance_m, priority)
values (
  '11111111-dddd-4111-8111-111111111111',
  '11111111-1111-4111-8111-111111111111',
  'Synthetic 5K A',
  '2027-02-01',
  5000,
  'C'
);
select (
  exists(select 1 from app.target_races where id = '11111111-aaaa-4111-8111-111111111111')
  and exists(select 1 from app.target_races where id = '11111111-dddd-4111-8111-111111111111')
  and not exists(select 1 from app.target_races where id = '22222222-bbbb-4222-8222-222222222222')
)::boolean as owner_a_insert_ok \gset
set local role postgres;
select ok(:'owner_a_insert_ok'::boolean, 'owner A can insert its own row');

set local role rp_api;
select set_config('request.jwt.claim.sub', '22222222-2222-4222-8222-222222222222', true);
select (count(*) = 1)::boolean as owner_b_profile_count_ok,
       (min(display_name) = 'Synthetic Athlete B')::boolean as owner_b_profile_value_ok
from app.athlete_profiles \gset
select (
  exists(select 1 from app.target_races where id = '22222222-bbbb-4222-8222-222222222222')
  and not exists(select 1 from app.target_races where id in (
    '11111111-aaaa-4111-8111-111111111111',
    '11111111-dddd-4111-8111-111111111111'))
)::boolean as owner_b_race_count_ok \gset
set local role postgres;

select ok(:'owner_b_profile_count_ok'::boolean, 'owner B sees only its profile');
select ok(:'owner_b_profile_value_ok'::boolean, 'owner B cannot see owner A profile');
select ok(:'owner_b_race_count_ok'::boolean, 'owner B cannot see either owner A race');

commit;

begin;
set local role postgres;
set local search_path = extensions, public;
set local role rp_api;
select (nullif(current_setting('request.jwt.claim.sub', true), '') is null)::boolean as owner_context_cleared,
       ((select count(*) from app.athlete_profiles) = 0)::boolean as unscoped_rows_hidden
\gset
set local role postgres;

select ok(:'owner_context_cleared'::boolean, 'transaction-local owner context was cleared');
select ok(:'unscoped_rows_hidden'::boolean, 'pooled connection without owner context sees no rows');
select * from finish();

delete from app.target_races
where id = '11111111-dddd-4111-8111-111111111111';

commit;
