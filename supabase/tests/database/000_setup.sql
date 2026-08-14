create extension if not exists pgtap with schema extensions;

begin;
set local role postgres;
set local search_path = extensions, public;

select plan(1);

select ok(true, 'pgTAP test dependency is available');

select * from finish();

rollback;
