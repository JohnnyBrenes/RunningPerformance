insert into auth.users (
  instance_id,
  id,
  aud,
  role,
  email,
  encrypted_password,
  email_confirmed_at,
  confirmation_token,
  recovery_token,
  email_change_token_new,
  email_change,
  raw_app_meta_data,
  raw_user_meta_data,
  created_at,
  updated_at
)
values
  (
    '00000000-0000-0000-0000-000000000000',
    '11111111-1111-4111-8111-111111111111',
    'authenticated',
    'authenticated',
    'athlete-a@example.invalid',
    extensions.crypt('synthetic-only-a', extensions.gen_salt('bf')),
    now(),
    '',
    '',
    '',
    '',
    '{"provider":"email","providers":["email"]}',
    '{"synthetic":true}',
    now(),
    now()
  ),
  (
    '00000000-0000-0000-0000-000000000000',
    '22222222-2222-4222-8222-222222222222',
    'authenticated',
    'authenticated',
    'athlete-b@example.invalid',
    extensions.crypt('synthetic-only-b', extensions.gen_salt('bf')),
    now(),
    '',
    '',
    '',
    '',
    '{"provider":"email","providers":["email"]}',
    '{"synthetic":true}',
    now(),
    now()
  )
on conflict (id) do nothing;

insert into auth.identities (
  provider_id,
  user_id,
  identity_data,
  provider,
  last_sign_in_at,
  created_at,
  updated_at
)
values
  (
    '11111111-1111-4111-8111-111111111111',
    '11111111-1111-4111-8111-111111111111',
    '{"sub":"11111111-1111-4111-8111-111111111111","email":"athlete-a@example.invalid","email_verified":true,"phone_verified":false}',
    'email',
    now(),
    now(),
    now()
  ),
  (
    '22222222-2222-4222-8222-222222222222',
    '22222222-2222-4222-8222-222222222222',
    '{"sub":"22222222-2222-4222-8222-222222222222","email":"athlete-b@example.invalid","email_verified":true,"phone_verified":false}',
    'email',
    now(),
    now(),
    now()
  )
on conflict (provider_id, provider) do nothing;

insert into app.athlete_profiles (owner_id, display_name, timezone_name, locale)
values
  ('11111111-1111-4111-8111-111111111111', 'Synthetic Athlete A', 'America/Mexico_City', 'es-MX'),
  ('22222222-2222-4222-8222-222222222222', 'Synthetic Athlete B', 'America/Mexico_City', 'es-MX')
on conflict (owner_id) do nothing;

insert into app.target_races (id, owner_id, name, race_date, distance_m, priority)
values
  ('11111111-aaaa-4111-8111-111111111111', '11111111-1111-4111-8111-111111111111', 'Synthetic 10K A', '2027-01-10', 10000, 'B'),
  ('22222222-bbbb-4222-8222-222222222222', '22222222-2222-4222-8222-222222222222', 'Synthetic 10K B', '2027-01-10', 10000, 'B')
on conflict (id) do nothing;

update app.athlete_profiles
set sex = case owner_id
  when '11111111-1111-4111-8111-111111111111' then 'male'
  when '22222222-2222-4222-8222-222222222222' then 'female'
  else sex
end
where owner_id in (
  '11111111-1111-4111-8111-111111111111',
  '22222222-2222-4222-8222-222222222222');

insert into app.exercises (
  id, owner_id, slug, canonical_name, movement_pattern, equipment)
values
  ('11111111-1001-4101-8101-111111111111', '11111111-1111-4111-8111-111111111111', 'sentadilla-goblet', 'Sentadilla goblet', 'squat', 'Mancuerna o kettlebell'),
  ('11111111-1002-4102-8102-111111111111', '11111111-1111-4111-8111-111111111111', 'peso-muerto-rumano', 'Peso muerto rumano', 'hinge', 'Kettlebell'),
  ('11111111-1003-4103-8103-111111111111', '11111111-1111-4111-8111-111111111111', 'plancha-lateral', 'Plancha lateral', 'core', 'Colchoneta'),
  ('11111111-1004-4104-8104-111111111111', '11111111-1111-4111-8111-111111111111', 'pogos-tobillo', 'Pogos de tobillo', 'plyometric', 'Peso corporal'),
  ('22222222-1001-4101-8101-222222222222', '22222222-2222-4222-8222-222222222222', 'sentadilla-goblet', 'Sentadilla goblet', 'squat', 'Mancuerna o kettlebell'),
  ('22222222-1002-4102-8102-222222222222', '22222222-2222-4222-8222-222222222222', 'peso-muerto-rumano', 'Peso muerto rumano', 'hinge', 'Kettlebell'),
  ('22222222-1003-4103-8103-222222222222', '22222222-2222-4222-8222-222222222222', 'plancha-lateral', 'Plancha lateral', 'core', 'Colchoneta'),
  ('22222222-1004-4104-8104-222222222222', '22222222-2222-4222-8222-222222222222', 'pogos-tobillo', 'Pogos de tobillo', 'plyometric', 'Peso corporal')
on conflict (id) do nothing;

insert into app.exercise_revisions (
  id, owner_id, exercise_id, version_number, display_name,
  brief_description, setup, execution, safety_cues)
values
  ('11111111-1101-4101-8101-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1001-4101-8101-111111111111', 1, 'Sentadilla goblet', 'Fortalece piernas y tronco con una carga sostenida frente al pecho.', 'Pies a una anchura cómoda, carga pegada al pecho y apoyo completo del pie.', 'Lleva la cadera abajo entre los pies, acompaña con las rodillas y vuelve a subir con control.', 'Mantén el tronco largo, las rodillas alineadas con los pies y los talones apoyados.'),
  ('11111111-1102-4102-8102-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1002-4102-8102-111111111111', 1, 'Peso muerto rumano', 'Entrena la bisagra de cadera y la cadena posterior.', 'De pie, sostén una kettlebell con ambas manos cerca de los muslos y suaviza las rodillas.', 'Empuja la cadera atrás mientras la carga baja cerca de las piernas; extiende la cadera para volver.', 'Conserva la espalda neutra y detén el descenso cuando pierdas tensión de cadera.'),
  ('11111111-1103-4103-8103-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1003-4103-8103-111111111111', 1, 'Plancha lateral', 'Desarrolla estabilidad lateral del tronco y control pélvico.', 'Coloca el codo debajo del hombro, antebrazo firme y pies apilados; usa rodillas flexionadas para iniciar si hace falta.', 'Eleva la cadera hasta formar una línea larga y respira sin perder la posición.', 'Evita hundir la cadera o cargar el cuello; reduce el tiempo si tiembla la técnica.'),
  ('11111111-1104-4104-8104-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1004-4104-8104-111111111111', 1, 'Pogos de tobillo', 'Introduce reactividad elástica con saltos pequeños y rápidos.', 'De pie, postura alta, pies bajo la cadera y rodillas relajadas.', 'Realiza rebotes cortos desde los tobillos, aterrizando debajo del cuerpo con contacto silencioso.', 'Detén la serie si aumenta el impacto, pierdes el ritmo o aparece dolor.'),
  ('22222222-1101-4101-8101-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1001-4101-8101-222222222222', 1, 'Sentadilla goblet', 'Fortalece piernas y tronco con una carga sostenida frente al pecho.', 'Pies a una anchura cómoda, carga pegada al pecho y apoyo completo del pie.', 'Lleva la cadera abajo entre los pies, acompaña con las rodillas y vuelve a subir con control.', 'Mantén el tronco largo, las rodillas alineadas con los pies y los talones apoyados.'),
  ('22222222-1102-4102-8102-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1002-4102-8102-222222222222', 1, 'Peso muerto rumano', 'Entrena la bisagra de cadera y la cadena posterior.', 'De pie, sostén una kettlebell con ambas manos cerca de los muslos y suaviza las rodillas.', 'Empuja la cadera atrás mientras la carga baja cerca de las piernas; extiende la cadera para volver.', 'Conserva la espalda neutra y detén el descenso cuando pierdas tensión de cadera.'),
  ('22222222-1103-4103-8103-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1003-4103-8103-222222222222', 1, 'Plancha lateral', 'Desarrolla estabilidad lateral del tronco y control pélvico.', 'Coloca el codo debajo del hombro, antebrazo firme y pies apilados; usa rodillas flexionadas para iniciar si hace falta.', 'Eleva la cadera hasta formar una línea larga y respira sin perder la posición.', 'Evita hundir la cadera o cargar el cuello; reduce el tiempo si tiembla la técnica.'),
  ('22222222-1104-4104-8104-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1004-4104-8104-222222222222', 1, 'Pogos de tobillo', 'Introduce reactividad elástica con saltos pequeños y rápidos.', 'De pie, postura alta, pies bajo la cadera y rodillas relajadas.', 'Realiza rebotes cortos desde los tobillos, aterrizando debajo del cuerpo con contacto silencioso.', 'Detén la serie si aumenta el impacto, pierdes el ritmo o aparece dolor.')
on conflict (id) do nothing;

insert into app.exercise_media (
  id, owner_id, exercise_revision_id, position, asset_uri, alt_text,
  mime_type, source, author, license, sha256, presentation_sex, width_px, height_px)
values
  ('11111111-1201-4101-8101-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1101-4101-8101-111111111111', 1, '/assets/exercises/goblet-squat-male-v1.png', 'Hombre demostrando el inicio y la posición baja de una sentadilla goblet.', 'image/png', 'OpenAI image generation, 2026-08-12', 'Running Performance', 'Project-owned synthetic asset', 'c5be8a1532f5603523799699a61f9374c78bf69affa6596221e30518ee10759f', 'male', 1024, 1024),
  ('11111111-1202-4101-8101-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1101-4101-8101-111111111111', 2, '/assets/exercises/goblet-squat-female-v1.png', 'Mujer demostrando el inicio y la posición baja de una sentadilla goblet.', 'image/png', 'OpenAI image generation, 2026-08-12', 'Running Performance', 'Project-owned synthetic asset', 'ab117e6ab3e42967b53aa314f739bf7a552a3d3ad00fb027628575843b7f0286', 'female', 1024, 1024),
  ('11111111-1203-4102-8102-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1102-4102-8102-111111111111', 1, '/assets/exercises/romanian-deadlift-male-v1.png', 'Hombre demostrando el inicio y la bisagra de cadera del peso muerto rumano.', 'image/png', 'OpenAI image generation, 2026-08-12', 'Running Performance', 'Project-owned synthetic asset', 'cbb04b1281c9760dd274e6dfbb0a781794ea22e06cd8258346064b7a2e009e61', 'male', 1024, 1024),
  ('11111111-1204-4102-8102-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1102-4102-8102-111111111111', 2, '/assets/exercises/romanian-deadlift-female-v1.png', 'Mujer demostrando el inicio y la bisagra de cadera del peso muerto rumano.', 'image/png', 'OpenAI image generation, 2026-08-12', 'Running Performance', 'Project-owned synthetic asset', '5391eb3afccfa2cceb7f45aa11e10ff620a3cc0fba4a99ed52f6fdb818675e17', 'female', 1024, 1024),
  ('11111111-1205-4103-8103-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1103-4103-8103-111111111111', 1, '/assets/exercises/side-plank-male-v1.png', 'Hombre demostrando la preparación y la posición final de una plancha lateral.', 'image/png', 'OpenAI image generation, 2026-08-12', 'Running Performance', 'Project-owned synthetic asset', 'e923bb55e1802c2eb62d745e378abd2cc7c3954fffbc74715fcf6e249901abf5', 'male', 1024, 1024),
  ('11111111-1206-4103-8103-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1103-4103-8103-111111111111', 2, '/assets/exercises/side-plank-female-v1.png', 'Mujer demostrando la preparación y la posición final de una plancha lateral.', 'image/png', 'OpenAI image generation, 2026-08-12', 'Running Performance', 'Project-owned synthetic asset', '59648efa9ec952b7738453db5a1caabc23872f74983dd5513375b9afe8818731', 'female', 1024, 1024),
  ('11111111-1207-4104-8104-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1104-4104-8104-111111111111', 1, '/assets/exercises/ankle-pogos-male-v1.png', 'Hombre demostrando la postura inicial y un rebote vertical pequeño de pogos de tobillo.', 'image/png', 'OpenAI image generation, 2026-08-12', 'Running Performance', 'Project-owned synthetic asset', '6c8882346cf039726c6ae4bfb6cda9df0b6ebf26b3e464dbaaf31a1646318f4c', 'male', 1024, 1024),
  ('11111111-1208-4104-8104-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1104-4104-8104-111111111111', 2, '/assets/exercises/ankle-pogos-female-v1.png', 'Mujer demostrando la postura inicial y un rebote vertical pequeño de pogos de tobillo.', 'image/png', 'OpenAI image generation, 2026-08-12', 'Running Performance', 'Project-owned synthetic asset', 'b006a6d1f96e3e9266af53c270acd109d8e16e0a01ce6d8826491cbab76319dc', 'female', 1024, 1024),
  ('22222222-1201-4101-8101-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1101-4101-8101-222222222222', 1, '/assets/exercises/goblet-squat-male-v1.png', 'Hombre demostrando el inicio y la posición baja de una sentadilla goblet.', 'image/png', 'OpenAI image generation, 2026-08-12', 'Running Performance', 'Project-owned synthetic asset', 'c5be8a1532f5603523799699a61f9374c78bf69affa6596221e30518ee10759f', 'male', 1024, 1024),
  ('22222222-1202-4101-8101-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1101-4101-8101-222222222222', 2, '/assets/exercises/goblet-squat-female-v1.png', 'Mujer demostrando el inicio y la posición baja de una sentadilla goblet.', 'image/png', 'OpenAI image generation, 2026-08-12', 'Running Performance', 'Project-owned synthetic asset', 'ab117e6ab3e42967b53aa314f739bf7a552a3d3ad00fb027628575843b7f0286', 'female', 1024, 1024),
  ('22222222-1203-4102-8102-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1102-4102-8102-222222222222', 1, '/assets/exercises/romanian-deadlift-male-v1.png', 'Hombre demostrando el inicio y la bisagra de cadera del peso muerto rumano.', 'image/png', 'OpenAI image generation, 2026-08-12', 'Running Performance', 'Project-owned synthetic asset', 'cbb04b1281c9760dd274e6dfbb0a781794ea22e06cd8258346064b7a2e009e61', 'male', 1024, 1024),
  ('22222222-1204-4102-8102-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1102-4102-8102-222222222222', 2, '/assets/exercises/romanian-deadlift-female-v1.png', 'Mujer demostrando el inicio y la bisagra de cadera del peso muerto rumano.', 'image/png', 'OpenAI image generation, 2026-08-12', 'Running Performance', 'Project-owned synthetic asset', '5391eb3afccfa2cceb7f45aa11e10ff620a3cc0fba4a99ed52f6fdb818675e17', 'female', 1024, 1024),
  ('22222222-1205-4103-8103-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1103-4103-8103-222222222222', 1, '/assets/exercises/side-plank-male-v1.png', 'Hombre demostrando la preparación y la posición final de una plancha lateral.', 'image/png', 'OpenAI image generation, 2026-08-12', 'Running Performance', 'Project-owned synthetic asset', 'e923bb55e1802c2eb62d745e378abd2cc7c3954fffbc74715fcf6e249901abf5', 'male', 1024, 1024),
  ('22222222-1206-4103-8103-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1103-4103-8103-222222222222', 2, '/assets/exercises/side-plank-female-v1.png', 'Mujer demostrando la preparación y la posición final de una plancha lateral.', 'image/png', 'OpenAI image generation, 2026-08-12', 'Running Performance', 'Project-owned synthetic asset', '59648efa9ec952b7738453db5a1caabc23872f74983dd5513375b9afe8818731', 'female', 1024, 1024),
  ('22222222-1207-4104-8104-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1104-4104-8104-222222222222', 1, '/assets/exercises/ankle-pogos-male-v1.png', 'Hombre demostrando la postura inicial y un rebote vertical pequeño de pogos de tobillo.', 'image/png', 'OpenAI image generation, 2026-08-12', 'Running Performance', 'Project-owned synthetic asset', '6c8882346cf039726c6ae4bfb6cda9df0b6ebf26b3e464dbaaf31a1646318f4c', 'male', 1024, 1024),
  ('22222222-1208-4104-8104-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1104-4104-8104-222222222222', 2, '/assets/exercises/ankle-pogos-female-v1.png', 'Mujer demostrando la postura inicial y un rebote vertical pequeño de pogos de tobillo.', 'image/png', 'OpenAI image generation, 2026-08-12', 'Running Performance', 'Project-owned synthetic asset', 'b006a6d1f96e3e9266af53c270acd109d8e16e0a01ce6d8826491cbab76319dc', 'female', 1024, 1024)
on conflict (id) do nothing;

-- Exercise catalog expansion based on the synthetic gym inventory recorded on
-- 2026-08-13. The reported 27.5 kg ceiling is intentionally not prescribed:
-- the supported movement and a safe working load still require confirmation.
insert into app.exercises (
  id, owner_id, slug, canonical_name, movement_pattern, equipment)
values
  ('11111111-1005-4105-8105-111111111111', '11111111-1111-4111-8111-111111111111', 'step-up-mancuernas', 'Step-up con mancuernas', 'unilateral', 'Step estable y dos mancuernas de 5 kg'),
  ('11111111-1006-4106-8106-111111111111', '11111111-1111-4111-8111-111111111111', 'jalon-al-pecho', 'Jalón al pecho', 'pull', 'Multiestación: polea alta y barra'),
  ('11111111-1007-4107-8107-111111111111', '11111111-1111-4111-8111-111111111111', 'remo-sentado-polea', 'Remo sentado en polea', 'pull', 'Multiestación: polea baja y agarre cerrado'),
  ('11111111-1008-4108-8108-111111111111', '11111111-1111-4111-8111-111111111111', 'press-pallof-polea', 'Press Pallof medio arrodillado', 'core', 'Multiestación: polea baja, agarre individual y colchoneta'),
  ('11111111-1009-4109-8109-111111111111', '11111111-1111-4111-8111-111111111111', 'press-pecho-maquina', 'Press de pecho en máquina', 'push', 'Multiestación: asiento, respaldo y brazos de press'),
  ('11111111-1010-4110-8110-111111111111', '11111111-1111-4111-8111-111111111111', 'extension-rodilla-maquina', 'Extensión de rodilla en máquina', 'knee_extension', 'Multiestación: asiento, respaldo y módulo de piernas'),
  ('22222222-1005-4105-8105-222222222222', '22222222-2222-4222-8222-222222222222', 'step-up-mancuernas', 'Step-up con mancuernas', 'unilateral', 'Step estable y dos mancuernas de 5 kg'),
  ('22222222-1006-4106-8106-222222222222', '22222222-2222-4222-8222-222222222222', 'jalon-al-pecho', 'Jalón al pecho', 'pull', 'Multiestación: polea alta y barra'),
  ('22222222-1007-4107-8107-222222222222', '22222222-2222-4222-8222-222222222222', 'remo-sentado-polea', 'Remo sentado en polea', 'pull', 'Multiestación: polea baja y agarre cerrado'),
  ('22222222-1008-4108-8108-222222222222', '22222222-2222-4222-8222-222222222222', 'press-pallof-polea', 'Press Pallof medio arrodillado', 'core', 'Multiestación: polea baja, agarre individual y colchoneta'),
  ('22222222-1009-4109-8109-222222222222', '22222222-2222-4222-8222-222222222222', 'press-pecho-maquina', 'Press de pecho en máquina', 'push', 'Multiestación: asiento, respaldo y brazos de press'),
  ('22222222-1010-4110-8110-222222222222', '22222222-2222-4222-8222-222222222222', 'extension-rodilla-maquina', 'Extensión de rodilla en máquina', 'knee_extension', 'Multiestación: asiento, respaldo y módulo de piernas')
on conflict (id) do nothing;

insert into app.exercise_revisions (
  id, owner_id, exercise_id, version_number, display_name,
  brief_description, setup, execution, safety_cues)
values
  ('11111111-1105-4105-8105-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1005-4105-8105-111111111111', 1, 'Step-up con mancuernas', 'Desarrolla fuerza unilateral y control de cadera y rodilla útiles para correr.', 'Usa un step estable y bajo. Apoya todo el pie delantero y sostén una mancuerna de 5 kg a cada lado solo si puedes mantener el equilibrio.', 'Empuja el step con la pierna adelantada, sube sin impulsarte con la pierna de atrás y baja despacio. Completa un lado y cambia.', 'Mantén rodilla y pie alineados. Reduce la altura o hazlo sin carga si pierdes estabilidad; detente ante dolor.'),
  ('11111111-1106-4106-8106-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1006-4106-8106-111111111111', 1, 'Jalón al pecho', 'Fortalece espalda y control escapular para sostener una postura estable al correr.', 'Siéntate estable, toma la barra un poco más ancho que los hombros y elige una carga que permita repeticiones limpias.', 'Baja los hombros, lleva los codos hacia las costillas y acerca la barra a la parte alta del pecho; regresa con control.', 'No lleves la barra detrás del cuello ni te balancees. Evita arquear en exceso y detente si aparece dolor de hombro.'),
  ('11111111-1107-4107-8107-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1007-4107-8107-111111111111', 1, 'Remo sentado en polea', 'Refuerza espalda y postura con un tirón horizontal controlado.', 'Siéntate frente a la polea baja, apoya los pies con seguridad, suaviza las rodillas y mantén el pecho alto.', 'Lleva el agarre hacia las costillas bajas con los codos cerca del cuerpo; extiende los brazos despacio sin perder la postura.', 'No te impulses con el tronco ni redondees la espalda. Reduce la carga si necesitas balancearte.'),
  ('11111111-1108-4108-8108-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1008-4108-8108-111111111111', 1, 'Press Pallof medio arrodillado', 'Entrena al tronco para resistir rotación y mantener estable la pelvis.', 'Colócate de lado a la polea baja, con la rodilla interior sobre una colchoneta y el pie exterior apoyado. Lleva el agarre al esternón.', 'Extiende los brazos al frente sin girar el pecho ni la pelvis, pausa y vuelve con control. Trabaja ambos lados.', 'Usa poca carga al aprender. No arquees la espalda; acércate a la polea o reduce peso si el cuerpo gira.'),
  ('11111111-1109-4109-8109-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1009-4109-8109-111111111111', 1, 'Press de pecho en máquina', 'Fortalece el empuje del tren superior con el torso apoyado.', 'Ajusta la posición para que los agarres queden a la altura del pecho. Apoya espalda y pies, y mantén las muñecas neutras.', 'Empuja los agarres al frente sin bloquear los codos y regresa despacio hasta una amplitud cómoda.', 'Mantén los hombros bajos y evita abrir demasiado los codos o rebotar al regresar. Detente ante dolor.'),
  ('11111111-1110-4110-8110-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1010-4110-8110-111111111111', 1, 'Extensión de rodilla en máquina', 'Fortalece el cuádriceps con un movimiento simple y controlado.', 'Apoya la espalda, alinea la rodilla con el pivote y coloca el rodillo sobre la parte baja de las piernas, justo encima de los tobillos.', 'Extiende las rodillas sin bloquearlas, pausa brevemente y baja el rodillo de forma controlada.', 'Empieza ligero, no balancees la carga ni levantes la cadera. Usa un rango sin dolor y detente si la rodilla molesta.'),
  ('22222222-1105-4105-8105-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1005-4105-8105-222222222222', 1, 'Step-up con mancuernas', 'Desarrolla fuerza unilateral y control de cadera y rodilla útiles para correr.', 'Usa un step estable y bajo. Apoya todo el pie delantero y sostén una mancuerna de 5 kg a cada lado solo si puedes mantener el equilibrio.', 'Empuja el step con la pierna adelantada, sube sin impulsarte con la pierna de atrás y baja despacio. Completa un lado y cambia.', 'Mantén rodilla y pie alineados. Reduce la altura o hazlo sin carga si pierdes estabilidad; detente ante dolor.'),
  ('22222222-1106-4106-8106-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1006-4106-8106-222222222222', 1, 'Jalón al pecho', 'Fortalece espalda y control escapular para sostener una postura estable al correr.', 'Siéntate estable, toma la barra un poco más ancho que los hombros y elige una carga que permita repeticiones limpias.', 'Baja los hombros, lleva los codos hacia las costillas y acerca la barra a la parte alta del pecho; regresa con control.', 'No lleves la barra detrás del cuello ni te balancees. Evita arquear en exceso y detente si aparece dolor de hombro.'),
  ('22222222-1107-4107-8107-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1007-4107-8107-222222222222', 1, 'Remo sentado en polea', 'Refuerza espalda y postura con un tirón horizontal controlado.', 'Siéntate frente a la polea baja, apoya los pies con seguridad, suaviza las rodillas y mantén el pecho alto.', 'Lleva el agarre hacia las costillas bajas con los codos cerca del cuerpo; extiende los brazos despacio sin perder la postura.', 'No te impulses con el tronco ni redondees la espalda. Reduce la carga si necesitas balancearte.'),
  ('22222222-1108-4108-8108-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1008-4108-8108-222222222222', 1, 'Press Pallof medio arrodillado', 'Entrena al tronco para resistir rotación y mantener estable la pelvis.', 'Colócate de lado a la polea baja, con la rodilla interior sobre una colchoneta y el pie exterior apoyado. Lleva el agarre al esternón.', 'Extiende los brazos al frente sin girar el pecho ni la pelvis, pausa y vuelve con control. Trabaja ambos lados.', 'Usa poca carga al aprender. No arquees la espalda; acércate a la polea o reduce peso si el cuerpo gira.'),
  ('22222222-1109-4109-8109-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1009-4109-8109-222222222222', 1, 'Press de pecho en máquina', 'Fortalece el empuje del tren superior con el torso apoyado.', 'Ajusta la posición para que los agarres queden a la altura del pecho. Apoya espalda y pies, y mantén las muñecas neutras.', 'Empuja los agarres al frente sin bloquear los codos y regresa despacio hasta una amplitud cómoda.', 'Mantén los hombros bajos y evita abrir demasiado los codos o rebotar al regresar. Detente ante dolor.'),
  ('22222222-1110-4110-8110-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1010-4110-8110-222222222222', 1, 'Extensión de rodilla en máquina', 'Fortalece el cuádriceps con un movimiento simple y controlado.', 'Apoya la espalda, alinea la rodilla con el pivote y coloca el rodillo sobre la parte baja de las piernas, justo encima de los tobillos.', 'Extiende las rodillas sin bloquearlas, pausa brevemente y baja el rodillo de forma controlada.', 'Empieza ligero, no balancees la carga ni levantes la cadera. Usa un rango sin dolor y detente si la rodilla molesta.')
on conflict (id) do nothing;

insert into app.exercise_media (
  id, owner_id, exercise_revision_id, position, asset_uri, alt_text,
  mime_type, source, author, license, sha256, presentation_sex, width_px, height_px)
values
  ('11111111-1209-4105-8209-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1105-4105-8105-111111111111', 1, '/assets/exercises/dumbbell-step-up-male-v1.png', 'Hombre demostrando el inicio y la subida controlada de un step-up con dos mancuernas de 5 kg.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', '59c2ada43f00f580f2f57c2c55d846a3250f769c3389593461a44ff3cee7f559', 'male', 1024, 1024),
  ('11111111-1210-4105-8210-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1105-4105-8105-111111111111', 2, '/assets/exercises/dumbbell-step-up-female-v1.png', 'Mujer demostrando el inicio y la subida controlada de un step-up con dos mancuernas de 5 kg.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', '0980d3a809df5e409a4a699802b50d3f7be4f91b12fc8ea5580f07c2be065373', 'female', 1024, 1024),
  ('11111111-1211-4106-8211-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1106-4106-8106-111111111111', 1, '/assets/exercises/lat-pulldown-male-v1.png', 'Hombre demostrando el inicio y el final de un jalón al pecho en la multiestación.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', '558c53840039a873bf898311e1c67a3e27d922c61ffdfe6c172be370aa08ca92', 'male', 1024, 1024),
  ('11111111-1212-4106-8212-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1106-4106-8106-111111111111', 2, '/assets/exercises/lat-pulldown-female-v1.png', 'Mujer demostrando el inicio y el final de un jalón al pecho en la multiestación.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', '272027a70f087a0a816173f02894c8104ab02db865edce1b53041b848889cbd4', 'female', 1024, 1024),
  ('11111111-1213-4107-8213-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1107-4107-8107-111111111111', 1, '/assets/exercises/seated-cable-row-male-v1.png', 'Hombre demostrando el inicio y el tirón hacia las costillas de un remo sentado en polea baja.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', 'b11ece465f0e1bcea5a5419d5170df151a6e38639f3883ec291068203c5b1170', 'male', 1024, 1024),
  ('11111111-1214-4107-8214-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1107-4107-8107-111111111111', 2, '/assets/exercises/seated-cable-row-female-v1.png', 'Mujer demostrando el inicio y el tirón hacia las costillas de un remo sentado en polea baja.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', 'daa32be0dedd9e40aedd5d866279e58c5b478d1204bd3720e445c65cbc8e7917', 'female', 1024, 1024),
  ('11111111-1215-4108-8215-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1108-4108-8108-111111111111', 1, '/assets/exercises/pallof-press-male-v1.png', 'Hombre demostrando el inicio y la extensión de brazos de un press Pallof medio arrodillado.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', '2acb54f818261efcd3436b92c3ac6289c98c367d65434eb2e280c11fb571c98d', 'male', 1024, 1024),
  ('11111111-1216-4108-8216-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1108-4108-8108-111111111111', 2, '/assets/exercises/pallof-press-female-v1.png', 'Mujer demostrando el inicio y la extensión de brazos de un press Pallof medio arrodillado.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', 'b04813f78c15ad453f6fd88bed90373a2db0f203b86de83ef5f857484b9b10ec', 'female', 1024, 1024),
  ('11111111-1217-4109-8217-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1109-4109-8109-111111111111', 1, '/assets/exercises/machine-chest-press-male-v1.png', 'Hombre demostrando el inicio y el empuje controlado de un press de pecho en máquina.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', 'bcb1c67bbd46ed20f2c600c3b73cf74b365178729fe7c5990fd6fbdbd2105c40', 'male', 1024, 1024),
  ('11111111-1218-4109-8218-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1109-4109-8109-111111111111', 2, '/assets/exercises/machine-chest-press-female-v1.png', 'Mujer demostrando el inicio y el empuje controlado de un press de pecho en máquina.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', '5b2a1b6d94bc209fd1c85701b09b9bc519a4173d72d610954d44abd9b5829823', 'female', 1024, 1024),
  ('11111111-1219-4110-8219-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1110-4110-8110-111111111111', 1, '/assets/exercises/machine-knee-extension-male-v1.png', 'Hombre demostrando el inicio y la extensión controlada de rodillas en máquina.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', '83c2dec93ae1c6c14dd77a5835710b2331cca256ad85fa8d34c024665786c785', 'male', 1024, 1024),
  ('11111111-1220-4110-8220-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-1110-4110-8110-111111111111', 2, '/assets/exercises/machine-knee-extension-female-v1.png', 'Mujer demostrando el inicio y la extensión controlada de rodillas en máquina.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', '69cb29351af46f51e89295bb7b6cc2265b2003c60de02470a5c3b8d81d0d567b', 'female', 1024, 1024),
  ('22222222-1209-4105-8209-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1105-4105-8105-222222222222', 1, '/assets/exercises/dumbbell-step-up-male-v1.png', 'Hombre demostrando el inicio y la subida controlada de un step-up con dos mancuernas de 5 kg.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', '59c2ada43f00f580f2f57c2c55d846a3250f769c3389593461a44ff3cee7f559', 'male', 1024, 1024),
  ('22222222-1210-4105-8210-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1105-4105-8105-222222222222', 2, '/assets/exercises/dumbbell-step-up-female-v1.png', 'Mujer demostrando el inicio y la subida controlada de un step-up con dos mancuernas de 5 kg.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', '0980d3a809df5e409a4a699802b50d3f7be4f91b12fc8ea5580f07c2be065373', 'female', 1024, 1024),
  ('22222222-1211-4106-8211-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1106-4106-8106-222222222222', 1, '/assets/exercises/lat-pulldown-male-v1.png', 'Hombre demostrando el inicio y el final de un jalón al pecho en la multiestación.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', '558c53840039a873bf898311e1c67a3e27d922c61ffdfe6c172be370aa08ca92', 'male', 1024, 1024),
  ('22222222-1212-4106-8212-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1106-4106-8106-222222222222', 2, '/assets/exercises/lat-pulldown-female-v1.png', 'Mujer demostrando el inicio y el final de un jalón al pecho en la multiestación.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', '272027a70f087a0a816173f02894c8104ab02db865edce1b53041b848889cbd4', 'female', 1024, 1024),
  ('22222222-1213-4107-8213-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1107-4107-8107-222222222222', 1, '/assets/exercises/seated-cable-row-male-v1.png', 'Hombre demostrando el inicio y el tirón hacia las costillas de un remo sentado en polea baja.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', 'b11ece465f0e1bcea5a5419d5170df151a6e38639f3883ec291068203c5b1170', 'male', 1024, 1024),
  ('22222222-1214-4107-8214-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1107-4107-8107-222222222222', 2, '/assets/exercises/seated-cable-row-female-v1.png', 'Mujer demostrando el inicio y el tirón hacia las costillas de un remo sentado en polea baja.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', 'daa32be0dedd9e40aedd5d866279e58c5b478d1204bd3720e445c65cbc8e7917', 'female', 1024, 1024),
  ('22222222-1215-4108-8215-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1108-4108-8108-222222222222', 1, '/assets/exercises/pallof-press-male-v1.png', 'Hombre demostrando el inicio y la extensión de brazos de un press Pallof medio arrodillado.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', '2acb54f818261efcd3436b92c3ac6289c98c367d65434eb2e280c11fb571c98d', 'male', 1024, 1024),
  ('22222222-1216-4108-8216-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1108-4108-8108-222222222222', 2, '/assets/exercises/pallof-press-female-v1.png', 'Mujer demostrando el inicio y la extensión de brazos de un press Pallof medio arrodillado.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', 'b04813f78c15ad453f6fd88bed90373a2db0f203b86de83ef5f857484b9b10ec', 'female', 1024, 1024),
  ('22222222-1217-4109-8217-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1109-4109-8109-222222222222', 1, '/assets/exercises/machine-chest-press-male-v1.png', 'Hombre demostrando el inicio y el empuje controlado de un press de pecho en máquina.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', 'bcb1c67bbd46ed20f2c600c3b73cf74b365178729fe7c5990fd6fbdbd2105c40', 'male', 1024, 1024),
  ('22222222-1218-4109-8218-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1109-4109-8109-222222222222', 2, '/assets/exercises/machine-chest-press-female-v1.png', 'Mujer demostrando el inicio y el empuje controlado de un press de pecho en máquina.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', '5b2a1b6d94bc209fd1c85701b09b9bc519a4173d72d610954d44abd9b5829823', 'female', 1024, 1024),
  ('22222222-1219-4110-8219-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1110-4110-8110-222222222222', 1, '/assets/exercises/machine-knee-extension-male-v1.png', 'Hombre demostrando el inicio y la extensión controlada de rodillas en máquina.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', '83c2dec93ae1c6c14dd77a5835710b2331cca256ad85fa8d34c024665786c785', 'male', 1024, 1024),
  ('22222222-1220-4110-8220-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-1110-4110-8110-222222222222', 2, '/assets/exercises/machine-knee-extension-female-v1.png', 'Mujer demostrando el inicio y la extensión controlada de rodillas en máquina.', 'image/png', 'OpenAI image generation, 2026-08-13', 'Running Performance', 'Project-owned synthetic asset', '69cb29351af46f51e89295bb7b6cc2265b2003c60de02470a5c3b8d81d0d567b', 'female', 1024, 1024)
on conflict (id) do nothing;

insert into app.training_plans (
  id, owner_id, name, purpose, target_start, target_end)
values
  ('11111111-2000-4200-8200-111111111111', '11111111-1111-4111-8111-111111111111', 'Semana de base y fuerza', 'Combinar carrera fácil, fuerza, movilidad y reactividad sin comprometer la recuperación.', date_trunc('week', current_date)::date, date_trunc('week', current_date)::date + 6),
  ('22222222-2000-4200-8200-222222222222', '22222222-2222-4222-8222-222222222222', 'Semana de base y fuerza', 'Combinar carrera fácil, fuerza, movilidad y reactividad sin comprometer la recuperación.', date_trunc('week', current_date)::date, date_trunc('week', current_date)::date + 6)
on conflict (id) do nothing;

insert into app.training_plan_versions (
  id, owner_id, training_plan_id, version_number, period_start, period_end,
  status, rationale)
values
  ('11111111-2100-4200-8200-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-2000-4200-8200-111111111111', 1, date_trunc('week', current_date)::date, date_trunc('week', current_date)::date + 6, 'draft', 'Versión sintética inicial para validar el flujo completo.'),
  ('22222222-2100-4200-8200-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-2000-4200-8200-222222222222', 1, date_trunc('week', current_date)::date, date_trunc('week', current_date)::date + 6, 'draft', 'Versión sintética inicial para validar el flujo completo.')
on conflict (id) do nothing;

do $$
begin
if not exists (
  select 1 from app.planned_sessions
  where id = '11111111-2201-4201-8201-111111111111') then
insert into app.planned_sessions (
  id, owner_id, training_plan_version_id, scheduled_date, session_type,
  modality, objective, duration_seconds, target_rpe_min, target_rpe_max,
  terrain, warmup, main_set, recoveries, cooldown)
values
  ('11111111-2201-4201-8201-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-2100-4200-8200-111111111111', current_date, 'strength_mobility_plyometrics', 'mixed', 'Construir fuerza útil, control lateral y elasticidad de tobillo con técnica limpia.', 2700, 5, 7, 'Superficie estable', '8 min de movilidad dinámica y activación de pies.', 'Tres bloques ordenados de movilidad, fuerza y pliometría.', '60–90 s entre series; 2 min antes de los pogos.', '5 min caminando y respiración tranquila.'),
  ('11111111-2202-4202-8202-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-2100-4200-8200-111111111111', current_date + 2, 'easy_run', 'running', 'Acumular tiempo aeróbico cómodo, capaz de conversar.', 2400, 3, 4, 'Plano y predecible', '8 min muy suaves.', '30 min continuos a esfuerzo cómodo.', 'No aplica.', '2 min caminando y movilidad ligera.'),
  ('11111111-2203-4203-8203-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-2100-4200-8200-111111111111', current_date + 4, 'long_run', 'running', 'Extender la resistencia sin convertir el día en una prueba.', 4200, 3, 5, 'Ruta conocida con desnivel suave', '10 min progresivos.', '60 min estables; baja el esfuerzo en las subidas.', 'Camina 60 s si la técnica se degrada.', '5 min muy suaves.'),
  ('22222222-2201-4201-8201-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-2100-4200-8200-222222222222', current_date, 'strength_mobility_plyometrics', 'mixed', 'Construir fuerza útil, control lateral y elasticidad de tobillo con técnica limpia.', 2700, 5, 7, 'Superficie estable', '8 min de movilidad dinámica y activación de pies.', 'Tres bloques ordenados de movilidad, fuerza y pliometría.', '60–90 s entre series; 2 min antes de los pogos.', '5 min caminando y respiración tranquila.'),
  ('22222222-2202-4202-8202-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-2100-4200-8200-222222222222', current_date + 2, 'easy_run', 'running', 'Acumular tiempo aeróbico cómodo, capaz de conversar.', 2400, 3, 4, 'Plano y predecible', '8 min muy suaves.', '30 min continuos a esfuerzo cómodo.', 'No aplica.', '2 min caminando y movilidad ligera.'),
  ('22222222-2203-4203-8203-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-2100-4200-8200-222222222222', current_date + 4, 'long_run', 'running', 'Extender la resistencia sin convertir el día en una prueba.', 4200, 3, 5, 'Ruta conocida con desnivel suave', '10 min progresivos.', '60 min estables; baja el esfuerzo en las subidas.', 'Camina 60 s si la técnica se degrada.', '5 min muy suaves.')
on conflict (id) do nothing;
end if;
end
$$;

-- APP-012 synthetic provider readings. Database and Storage remain measured
-- directly; these append-only events prevent inventing egress, CI or backend use.
insert into app.audit_events (
  id, owner_id, actor_id, actor_type, action, entity_type,
  correlation_id, changed_fields, detail, occurred_at)
values
  (
    '11111111-5201-4521-8521-111111111111',
    '11111111-1111-4111-8111-111111111111',
    '11111111-1111-4111-8111-111111111111',
    'athlete', 'free_tier.usage_reported', 'free_tier',
    '11111111-5202-4522-8522-111111111111',
    array['egressGb', 'ciMinutes', 'backendHours', 'measuredAt'],
    jsonb_build_object(
      'egressGb', 0.20,
      'ciMinutes', 120,
      'backendHours', 45,
      'measuredAt', now(),
      'billingEnabled', false),
    now()),
  (
    '22222222-5201-4521-8521-222222222222',
    '22222222-2222-4222-8222-222222222222',
    '22222222-2222-4222-8222-222222222222',
    'athlete', 'free_tier.usage_reported', 'free_tier',
    '22222222-5202-4522-8522-222222222222',
    array['egressGb', 'ciMinutes', 'backendHours', 'measuredAt'],
    jsonb_build_object(
      'egressGb', 0.10,
      'ciMinutes', 60,
      'backendHours', 20,
      'measuredAt', now(),
      'billingEnabled', false),
    now())
on conflict (id) do nothing;

do $$
begin
if not exists (
  select 1 from app.planned_session_blocks
  where id = '11111111-2301-4301-8301-111111111111') then
insert into app.planned_session_blocks (
  id, owner_id, planned_session_id, position, block_type, repeat_count, instructions)
values
  ('11111111-2301-4301-8301-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-2201-4201-8201-111111111111', 1, 'mobility', 1, 'Controla la respiración y evita compensar con el cuello.'),
  ('11111111-2302-4302-8302-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-2201-4201-8201-111111111111', 2, 'circuit', 3, 'Alterna los dos ejercicios con carga moderada y técnica estable.'),
  ('11111111-2303-4303-8303-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-2201-4201-8201-111111111111', 3, 'circuit', 2, 'Prioriza aterrizajes silenciosos y una postura alta.'),
  ('22222222-2301-4301-8301-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-2201-4201-8201-222222222222', 1, 'mobility', 1, 'Controla la respiración y evita compensar con el cuello.'),
  ('22222222-2302-4302-8302-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-2201-4201-8201-222222222222', 2, 'circuit', 3, 'Alterna los dos ejercicios con carga moderada y técnica estable.'),
  ('22222222-2303-4303-8303-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-2201-4201-8201-222222222222', 3, 'circuit', 2, 'Prioriza aterrizajes silenciosos y una postura alta.')
on conflict (id) do nothing;
end if;
end
$$;

do $$
begin
if not exists (
  select 1 from app.planned_session_exercises
  where id = '11111111-2401-4401-8401-111111111111') then
insert into app.planned_session_exercises (
  id, owner_id, planned_session_block_id, exercise_revision_id, position,
  sets, repetitions_min, repetitions_max, duration_seconds, rest_seconds,
  target_rpe, target_rir, tempo, side, note)
values
  ('11111111-2401-4401-8401-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-2301-4301-8301-111111111111', '11111111-1103-4103-8103-111111111111', 1, 2, null, null, 25, 30, 6, 3, null, 'each', '25 s por lado.'),
  ('11111111-2402-4402-8402-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-2302-4302-8302-111111111111', '11111111-1101-4101-8101-111111111111', 1, 3, 8, 10, null, 75, 7, 3, '3-1-1', null, 'Carga que permita dos o tres repeticiones limpias en reserva.'),
  ('11111111-2403-4403-8403-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-2302-4302-8302-111111111111', '11111111-1102-4102-8102-111111111111', 2, 3, 8, 10, null, 75, 7, 3, '3-1-1', null, 'Mantén la carga cerca de las piernas.'),
  ('11111111-2404-4404-8404-111111111111', '11111111-1111-4111-8111-111111111111', '11111111-2303-4303-8303-111111111111', '11111111-1104-4104-8104-111111111111', 1, 2, 20, 20, null, 90, 6, 4, null, null, 'Cuenta contactos; corta antes si dejan de ser silenciosos.'),
  ('22222222-2401-4401-8401-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-2301-4301-8301-222222222222', '22222222-1103-4103-8103-222222222222', 1, 2, null, null, 25, 30, 6, 3, null, 'each', '25 s por lado.'),
  ('22222222-2402-4402-8402-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-2302-4302-8302-222222222222', '22222222-1101-4101-8101-222222222222', 1, 3, 8, 10, null, 75, 7, 3, '3-1-1', null, 'Carga que permita dos o tres repeticiones limpias en reserva.'),
  ('22222222-2403-4403-8403-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-2302-4302-8302-222222222222', '22222222-1102-4102-8102-222222222222', 2, 3, 8, 10, null, 75, 7, 3, '3-1-1', null, 'Mantén la carga cerca de las piernas.'),
  ('22222222-2404-4404-8404-222222222222', '22222222-2222-4222-8222-222222222222', '22222222-2303-4303-8303-222222222222', '22222222-1104-4104-8104-222222222222', 1, 2, 20, 20, null, 90, 6, 4, null, null, 'Cuenta contactos; corta antes si dejan de ser silenciosos.')
on conflict (id) do nothing;
end if;
end
$$;

update app.training_plan_versions v
set status = 'superseded'
where v.id in (
  '11111111-2100-4200-8200-111111111111',
  '22222222-2100-4200-8200-222222222222')
  and exists (
    select 1 from app.training_plan_versions current_version
    where current_version.owner_id = v.owner_id
      and current_version.status = 'published'
      and current_version.id <> v.id);

update app.training_plan_versions v
set status = 'published', published_at = now()
where v.id in (
  '11111111-2100-4200-8200-111111111111',
  '22222222-2100-4200-8200-222222222222')
  and not exists (
    select 1 from app.training_plan_versions current_version
    where current_version.owner_id = v.owner_id
      and current_version.status = 'published');

-- APP-010 synthetic split session: two source activities, one planned outcome,
-- one immediate check-in and therefore one logical sRPE load.
insert into app.activities (
  id, owner_id, provisional_activity_key, garmin_activity_id,
  activity_type, activity_category, modality, started_at_local, title,
  distance_m, duration_seconds, validation_status)
values
  (
    '11111111-3001-4301-8301-111111111111',
    '11111111-1111-4111-8111-111111111111',
    'seed-app010-split-a', 99000000001,
    'strength_training', 'strength', 'indoor', current_date + time '06:00',
    'Bloque de fuerza sintético A', null, 600, 'published'),
  (
    '11111111-3002-4302-8302-111111111111',
    '11111111-1111-4111-8111-111111111111',
    'seed-app010-split-b', 99000000002,
    'strength_training', 'strength', 'indoor', current_date + time '06:12',
    'Bloque de fuerza sintético B', null, 900, 'published'),
  (
    '11111111-3003-4303-8303-111111111111',
    '11111111-1111-4111-8111-111111111111',
    'seed-app010-run-candidate', 99000000003,
    'running', 'running', 'treadmill', current_date + 2 + time '06:00',
    'Carrera fácil sintética', 6500, 2400, 'published')
on conflict (id) do nothing;

insert into app.activity_session_links (
  id, owner_id, activity_id, planned_session_id, method, criteria,
  status, actor_id)
values
  (
    '11111111-3101-4311-8311-111111111111',
    '11111111-1111-4111-8111-111111111111',
    '11111111-3001-4301-8301-111111111111',
    '11111111-2201-4201-8201-111111111111',
    'manual', '{"source":"synthetic_seed"}', 'confirmed',
    '11111111-1111-4111-8111-111111111111'),
  (
    '11111111-3102-4312-8312-111111111111',
    '11111111-1111-4111-8111-111111111111',
    '11111111-3002-4302-8302-111111111111',
    '11111111-2201-4201-8201-111111111111',
    'manual', '{"source":"synthetic_seed"}', 'confirmed',
    '11111111-1111-4111-8111-111111111111')
on conflict (id) do nothing;

insert into app.planned_session_outcomes (
  owner_id, planned_session_id, execution_status, confirmed_at)
values (
  '11111111-1111-4111-8111-111111111111',
  '11111111-2201-4201-8201-111111111111',
  'completed_as_planned', now())
on conflict (planned_session_id) do nothing;

insert into app.session_checkins (
  owner_id, planned_session_id, checkin_window, session_rpe, pain,
  gait_changed, fatigue, sleep_quality, perceived_recovery,
  has_illness_or_symptom, recorded_at)
values (
  '11111111-1111-4111-8111-111111111111',
  '11111111-2201-4201-8201-111111111111',
  'immediate', 5, 0, false, 4, 5, 7, false, now())
on conflict (planned_session_id, checkin_window)
  where planned_session_id is not null
do nothing;

-- APP-011 synthetic provisional snapshot. It is intentionally yellow because
-- the strength response at 24-48 hours and two mandatory outcomes are ND.
do $$
begin
  if not exists (
    select 1
    from app.weekly_evaluations
    where owner_id = '11111111-1111-4111-8111-111111111111'
      and week_start = date_trunc('week', current_date)::date) then
    perform set_config(
      'request.jwt.claim.sub',
      '11111111-1111-4111-8111-111111111111',
      true);
    perform app.create_weekly_evaluation_snapshot(
      date_trunc('week', current_date)::date,
      'provisional',
      '11111111-5000-4500-8500-111111111111');
  end if;
end
$$;
