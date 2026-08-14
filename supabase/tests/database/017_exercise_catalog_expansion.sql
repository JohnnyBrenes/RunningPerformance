begin;
set local role postgres;
set local search_path = extensions, public;
grant usage on schema extensions to rp_api;

select plan(8);

select is(
  (select count(*)::integer from app.exercises
   where owner_id = '11111111-1111-4111-8111-111111111111'),
  10,
  'synthetic athlete A has the expanded ten-exercise catalog'
);

select is(
  (select count(*)::integer from app.exercises
   where owner_id = '22222222-2222-4222-8222-222222222222'),
  10,
  'synthetic athlete B has the expanded ten-exercise catalog'
);

select is(
  (select count(*)::integer
   from app.exercise_revisions r
   join app.exercises e on e.id = r.exercise_id
   where e.owner_id = '11111111-1111-4111-8111-111111111111'
     and e.slug in ('step-up-mancuernas', 'jalon-al-pecho', 'remo-sentado-polea',
                    'press-pallof-polea', 'press-pecho-maquina', 'extension-rodilla-maquina')),
  6,
  'each added exercise has one immutable revision'
);

select is(
  (select count(*)::integer
   from app.exercise_media m
   join app.exercise_revisions r on r.id = m.exercise_revision_id
   join app.exercises e on e.id = r.exercise_id
   where e.owner_id = '11111111-1111-4111-8111-111111111111'
     and e.slug in ('step-up-mancuernas', 'jalon-al-pecho', 'remo-sentado-polea',
                    'press-pallof-polea', 'press-pecho-maquina', 'extension-rodilla-maquina')),
  12,
  'each added exercise retains masculine and feminine visual guidance'
);

select ok(
  (select bool_and(
     m.presentation_sex in ('female', 'male')
     and m.width_px = 1024
     and m.height_px = 1024
     and length(m.sha256) = 64)
   from app.exercise_media m
   join app.exercise_revisions r on r.id = m.exercise_revision_id
   join app.exercises e on e.id = r.exercise_id
   where e.owner_id = '11111111-1111-4111-8111-111111111111'
     and e.slug in ('step-up-mancuernas', 'jalon-al-pecho', 'remo-sentado-polea',
                    'press-pallof-polea', 'press-pecho-maquina', 'extension-rodilla-maquina')),
  'added exercise media retains dimensions, profile presentation and checksums'
);

select ok(
  not exists (
    select 1
    from app.exercise_revisions r
    join app.exercises e on e.id = r.exercise_id
    where e.slug in ('step-up-mancuernas', 'jalon-al-pecho', 'remo-sentado-polea',
                     'press-pallof-polea', 'press-pecho-maquina', 'extension-rodilla-maquina')
      and concat_ws(' ', e.equipment, r.setup, r.execution, r.safety_cues) like '%27.5%'),
  'the unconfirmed 27.5 kg ceiling is not converted into a prescription'
);

set local role rp_api;
select set_config('request.jwt.claim.sub', '11111111-1111-4111-8111-111111111111', true);

select is(
  (select count(*)::integer from app.exercises),
  10,
  'RLS exposes only the signed-in athlete exercise catalog'
);

select is(
  (select count(*)::integer from app.exercises
   where owner_id = '22222222-2222-4222-8222-222222222222'),
  0,
  'RLS hides the other synthetic athlete exercise catalog'
);

select * from finish();
rollback;
