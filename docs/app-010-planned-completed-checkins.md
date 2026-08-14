# APP-010 — Planificado-realizado y captura subjetiva

**Estado:** completada el 2026-08-13  
**Versión:** `APP-010-v1-2026-08-13`  
**Incremento:** I6

## Resultado

El flujo planificado-realizado quedó operativo de punta a punta contra Supabase local. Una sesión publicada puede relacionarse con una o varias actividades Garmin sin contarlas como sesiones independientes, registrar exactamente uno de los cinco resultados de TRN-003 y conservar check-ins inmediatos, de 24 h y de 48 h sin convertir ausencias en cero.

La carga interna se calcula una sola vez por sesión lógica: minutos totales de las actividades confirmadas multiplicados por un único RPE global inmediato. El fixture sintético vincula dos actividades de fuerza de 10 y 15 minutos a una sesión; el resultado visible es una sesión, dos archivos, 25 minutos y sRPE 125 con RPE 5.

## Vínculos y sesión lógica

- La propuesta automática sólo se crea cuando existe un candidato único en la fecha local exacta, con categoría compatible y sin otro vínculo activo.
- La selección manual admite varias actividades para la misma sesión y también mover una actividad desde otra sesión.
- Los cambios no eliminan historia: los vínculos `proposed` o `confirmed` pasan a `withdrawn` o `rejected`, y el reemplazo conserva `supersedes_id`.
- Índices parciales impiden más de un vínculo activo por actividad y más de un check-in por sesión/ventana.
- El borrador del plan no acepta captura; una versión publicada o sustituida conserva su contexto histórico.
- RLS deriva siempre el propietario del JWT y las vistas usan `security_invoker`.

## Resultado TRN-003 y check-ins

Los estados persistidos son únicamente:

- `completed_as_planned`;
- `completed_modified`;
- `valid_substitution`;
- `not_completed`;
- `optional_not_completed`.

Modificación, sustitución o sesión no realizada exigen motivo. `optional_not_completed` sólo es válido para una sesión opcional.

Cada ventana mantiene por separado RPE global, dolor y localización, cambio de zancada, fatiga, calidad del sueño, recuperación percibida, enfermedad/síntoma, respuesta posterior y nota. El RPE sólo puede guardarse en la ventana inmediata; la respuesta `normal`, `incomplete` o `adverse` sólo en 24/48 h. Valores desconocidos permanecen `NULL`/ND.

## Superficie entregada

- `GET /api/v1/sessions/{sessionId}/completion`.
- `POST /api/v1/sessions/{sessionId}/links/proposals`.
- `POST /api/v1/sessions/{sessionId}/links`.
- `PUT /api/v1/sessions/{sessionId}/links/{linkId}`.
- `PUT /api/v1/sessions/{sessionId}/outcome`.
- `PUT /api/v1/sessions/{sessionId}/checkins/{checkinWindow}`.
- Vista `app.v_logical_session_srpe` y ampliación de `app.v_planned_vs_completed`.
- OpenAPI 3.1 y cliente TypeScript regenerados.
- Panel responsive integrado en `/plan`, con resumen lógico, candidatos, historial, resultado y check-ins.

## Evidencia

- Las 15 migraciones se aplicaron desde cero con seed exclusivamente sintético.
- Lint del esquema `app` sin errores.
- 95/95 pruebas pgTAP, incluidas agregación de dos actividades, sRPE único, cinco estados, ventanas, versionado y RLS.
- 13/13 pruebas unitarias y 2/2 de integración .NET.
- Build Release de la API sin warnings y contrato OpenAPI generado desde el ensamblado compilado.
- 9/9 pruebas Vitest y comprobación TypeScript.
- Build Vite de producción completado.
- 9/9 escenarios Playwright en Chromium 320 px, WebKit 390 px y Chromium escritorio; el flujo guardó una respuesta normal de 24 h y permaneció sin scroll horizontal.
- `GET /health/ready` respondió 200 durante el E2E.

## Alcance y continuación

No se modificó producción, no se importaron datos reales ni se habilitó ningún componente pagado. `APP-011` queda como siguiente tarea técnica elegible para I7, todavía sin activar. `MON-001` continúa de forma independiente porque su cierre depende de nuevos datos del atleta.

