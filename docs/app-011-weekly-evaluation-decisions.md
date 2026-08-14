# APP-011 — Evaluación semanal, decisión y ajuste

**Versión:** `APP-011-v1-2026-08-13`  
**Incremento:** I7  
**Estado:** completado localmente con datos exclusivamente sintéticos

## Resultado

I7 cierra una semana mediante un snapshot inmutable de P1–P5, calcula un semáforo explicable con la peor señal disponible, conserva ausencias como `NULL`/ND y permite recorrer cada métrica hasta su sesión planificada, actividad o check-in de origen. El sistema no ajusta el plan por sí solo: una decisión humana con narrativa obligatoria queda auditada y, cuando corresponde, clona una nueva versión en borrador sin modificar la publicación anterior.

## Contrato funcional

- P1 conserva los cinco resultados TRN-003 y calcula cumplimiento estricto sin convertir ausencias en cero.
- P2 separa distancia, duración y ritmo planificado/realizado, incluido interior/exterior; lo desconocido continúa como `NULL`/ND.
- P3 conserva la observación explícita de tirada larga y sus componentes, incluso cuando faltan datos.
- P4 agrega sRPE por sesión lógica y por grupo running/otro/total sin contar dos veces actividades vinculadas.
- P5 conserva dolor, zancada, fatiga, sueño, recuperación, enfermedad y síntomas como señales independientes; no produce una puntuación clínica compuesta.
- El semáforo `green`/`yellow`/`red` aplica la peor señal y guarda razones explicables junto al snapshot.
- Toda métrica tiene evidencia navegable; el detalle enlaza la versión y sesión del plan de origen.
- Las decisiones `execute`, `adapt`, `reduce` y `stop` exigen decisión, motivo, riesgos y seguimiento humanos.
- `adapt` y `reduce` exigen un ajuste exacto. La API clona una nueva versión `draft`, remapea sesiones e inserta el antes/después auditado; las versiones publicadas son inmutables.

## Superficie entregada

- Migración `0150_weekly_evaluation_decisions.sql` con restricciones, privilegios, RLS, snapshot transaccional y auditoría.
- API autenticada:
  - `GET /api/v1/evaluations`
  - `GET /api/v1/evaluations/{evaluationId}`
  - `POST /api/v1/evaluations/snapshots`
  - `POST /api/v1/evaluations/{evaluationId}/decisions`
- OpenAPI y cliente TypeScript regenerados.
- Pantalla responsive `/evaluations` con P1–P5, ND explícito, semáforo, evidencia desplegable y formulario de decisión/ajuste.
- Fixtures y seed locales exclusivamente sintéticos.

## Validación

| Puerta | Resultado |
|---|---:|
| Migraciones locales | 16 |
| Lint de base de datos | 0 errores |
| pgTAP | 111 aprobadas |
| Contrato de esquema | 45 tablas, 9 vistas, RLS cubierto |
| .NET unitarias | 20 aprobadas |
| .NET integración | 2 aprobadas |
| Build .NET Release | 0 errores, 0 advertencias |
| Vitest | 12 aprobadas |
| TypeScript/lint | aprobado |
| Build Vite | aprobado |
| Playwright | 10 aprobadas, 2 omitidas intencionalmente |

Playwright cubrió Chromium a 320 px, WebKit a 390 px y Chromium de escritorio. El ajuste de versión sólo se ejecuta en escritorio; por eso ese caso se omite en los dos perfiles compactos. La API local respondió correctamente durante la prueba de extremo a extremo.

## Límites preservados

- No se modificó producción.
- No se importaron ni consultaron datos deportivos reales.
- No se modificaron actividades Garmin.
- No se habilitaron componentes pagados.
- `APP-012` permanece pendiente y no fue activada.
- `MON-001` no cambió porque no llegaron datos deportivos nuevos.

