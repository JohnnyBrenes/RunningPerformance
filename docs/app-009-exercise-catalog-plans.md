# APP-009 — Catálogo visual y plan versionado

**Estado:** completada el 2026-08-12  
**Versión:** `APP-009-v1-2026-08-12`  
**Incremento:** I3

## Resultado

El catálogo de ejercicios y el plan semanal están operativos de punta a punta contra Supabase local en Docker. El usuario puede consultar la sesión de hoy, recorrer el calendario, leer cada bloque y su dosificación, abrir la guía técnica, crear un borrador desde la publicación actual, ajustar fecha/objetivo y publicar una nueva versión sin reescribir la anterior.

El perfil incorpora `female`, `male` o `unspecified` exclusivamente para elegir la variante visual. Las instrucciones, la prescripción y las reglas de seguridad no cambian. El usuario sintético A usa la variante masculina y B la femenina.

## Catálogo y recursos

- Cuatro ejercicios sintéticos: sentadilla goblet, peso muerto rumano, plancha lateral y pogos de tobillo.
- Revisiones textuales inmutables con descripción breve, preparación, ejecución y seguridad.
- Los cuatro ejercicios incluyen dos PNG de 1024 × 1024: hombre y mujer.
- Cada medio registra variante, posición, URI versionada, texto alternativo, MIME, fuente, autor, licencia, dimensiones y SHA-256.
- La web carga imágenes de forma diferida y siempre presenta el contenido técnico aun si el medio no existe o no carga.

Los ocho activos finales están en `src/web/public/assets/exercises/`:

- `goblet-squat-male-v1.png` / `goblet-squat-female-v1.png`;
- `romanian-deadlift-male-v1.png` / `romanian-deadlift-female-v1.png`;
- `side-plank-male-v1.png` / `side-plank-female-v1.png`.
- `ankle-pogos-male-v1.png` / `ankle-pogos-female-v1.png`.

Se usó `imagegen` en modo generación y edición. El prompt base pidió láminas científicas-educativas de dos posiciones, fondo marfil, línea verde bosque, acento ámbar, anatomía plausible, técnica segura, cuerpo completo y ausencia de texto/logos. Las ediciones masculina/femenina conservaron composición y ejercicio, cambiando únicamente la presentación del atleta. Los originales generados permanecen en el directorio administrado de Codex y las copias versionadas viven en el proyecto.

## Versionado e inmutabilidad

- Las sesiones, bloques y ejercicios sólo admiten cambios mientras su versión padre está en `draft`.
- Triggers rechazan `INSERT`, `UPDATE` y `DELETE` sobre el contenido de una versión publicada, sustituida o archivada.
- `app.clone_training_plan_draft` bloquea el plan, calcula la siguiente versión, clona sesiones/bloques/ejercicios y escribe auditoría.
- `app.publish_training_plan_version` exige al menos una sesión, sustituye la publicación anterior, publica el borrador y audita la transición.
- Ambas funciones derivan el propietario del contexto RLS y sólo `rp_api` puede ejecutarlas; `rp_worker`, `anon`, `authenticated` y `public` no reciben acceso.
- La validación integral publicó `v2` del plan sintético A y conservó `v1` como `superseded`.

## Superficie entregada

- `GET /api/v1/exercises` y `GET /api/v1/exercises/{id}`.
- `GET /api/v1/plans`, `GET /api/v1/plans/current` y `GET /api/v1/plans/{planId}/versions/{versionId}`.
- `POST /api/v1/plans/{planId}/drafts`.
- `PUT /api/v1/plans/{planId}/versions/{versionId}/sessions/{sessionId}`.
- `POST /api/v1/plans/{planId}/versions/{versionId}/publish`.
- OpenAPI 3.1 y cliente TypeScript regenerados.
- Nuevas rutas responsive `/plan` y `/exercises`; el logout móvil queda visible como `Salir` en el encabezado.

## Evidencia

- Migración `0100` aplicada de forma no destructiva y lint SQL sin errores.
- 57/57 pruebas pgTAP, incluidas selección de medios, ausencia de medio mediante fila temporal, orden de copia, aislamiento entre propietarios e inmutabilidad profunda publicada.
- Build .NET Release sin warnings; 8/8 pruebas unitarias y 2/2 de integración.
- 7/7 pruebas Vitest, incluidas selección por sexo y fallback sin imagen.
- 9/9 escenarios Playwright: Chromium 320 px, WebKit 390 px y Chromium escritorio.
- En cada viewport se comprobaron perfil, carreras, variante masculina, catálogo, plan publicado, sesión completa, logout y ausencia de scroll horizontal.
- Build Vite de producción completado.
- Smoke autenticado: 4 ejercicios, 2 medios en cada ejercicio, 3 sesiones, 3 bloques en la sesión mixta y ciclo `clone → update → publish` con dos versiones retenidas.

## Operación local

El frontend continúa en `http://127.0.0.1:5173` y la API en `http://127.0.0.1:5080`. Las cuentas sintéticas siguen siendo:

- `athlete-a@example.invalid` / `synthetic-only-a` — ilustraciones masculinas;
- `athlete-b@example.invalid` / `synthetic-only-b` — ilustraciones femeninas.

No se creó producción, no se importaron datos reales y no se habilitó ningún componente pagado. `APP-006` queda como siguiente tarea técnica, todavía sin activar.

## Ampliación práctica local — 2026-08-13

El catálogo sintético creció de 4 a 10 ejercicios sin modificar versiones publicadas del plan:

- step-up con dos mancuernas confirmadas de 5 kg;
- jalón al pecho en polea alta;
- remo sentado en polea baja;
- press Pallof medio arrodillado desde polea baja;
- press de pecho en los brazos articulados de la multiestación;
- extensión de rodilla en el módulo de piernas.

Cada ficha incluye propósito, preparación, ejecución, seguridad, equipo y dos ilustraciones 1024×1024 seleccionables por el sexo del perfil. Las fotos del gimnasio solo guiaron la generación y permanecen fuera de la aplicación. Los 27.5 kg reportados no aparecen como prescripción porque todavía no se confirmó a qué movimiento corresponden.

La carga se aplicó de manera aditiva, sin `db reset`: permanecen 460 actividades importadas y 3 sintéticas. Pasaron 8/8 pruebas pgTAP específicas con RLS, 17/17 Vitest, TypeScript/OpenAPI y 1/1 caso Playwright dirigido en Chromium escritorio.
