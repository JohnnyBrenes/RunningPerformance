# Plan de implementación — Running Performance App

**Versión:** `APP-004-v3-2026-08-14`  
**Estado:** aprobado para ejecución incremental  
**Contratos de entrada:** `APP-001-v5-2026-08-14`, `APP-002-v3-2026-08-12` y `APP-003-v3-2026-08-14`

## 1. Resultado ejecutivo

La aplicación se construirá en diez incrementos verticales, cada uno desplegable y verificable con datos sintéticos. El camino crítico es:

```text
I0 cimientos
 → I1 esquema/RLS
 → I2 acceso y shell
 → I3 ejercicios y plan
 → I4 CSV histórico
 → I5 FIT incremental
 → I6 planificado/realizado y check-ins
 → I7 evaluación y decisiones
 → I8 dashboard y exportación
 → I9 endurecimiento y producción
```

No se aprovisionará producción ni se cargará información real hasta automatizar RLS negativa entre dos propietarios, idempotencia, detección de secretos/datos personales y restauración. `App/` sigue siendo el único límite publicable. Toda pieza necesaria para publicar el MVP debe permanecer en USD 0: sin tarjeta, prueba temporal, add-on pagado, dominio comprado ni escalado automático con cobro.

La siguiente tarea es `APP-005`. No queda activada automáticamente por el cierre de este plan.

## 2. Decisiones de implementación

### 2.1 Repositorio y dependencias

- Un solo repositorio desde `App/`, una solución .NET y un `package-lock.json` para la SPA.
- Central Package Management mediante `Directory.Packages.props` y archivos `packages.lock.json` en modo bloqueado.
- npm usa versiones directas exactas, sin `^` ni `~`; el lockfile fija también dependencias transitivas.
- `global.json`, `.nvmrc` y `packageManager` fijan SDK, Node y npm. Las actualizaciones entran en PR independiente y deben repetir todas las puertas.
- Imágenes de contenedor multi-stage, usuario no root, filesystem de aplicación de solo lectura cuando el proveedor lo permita y tag más digest inmutable resuelto en `I0`.
- No se incorpora un framework de componentes. Tailwind aporta tokens/utilidades; los componentes accesibles son propios y pequeños.
- OpenAPI es el contrato de API y genera el cliente TypeScript; no se duplican DTOs a mano.

### 2.2 Hosting seleccionado

| Pieza | Decisión inicial |
|---|---|
| Repositorio y CI | GitHub Free; repositorio privado durante el desarrollo y publicación solo de `App/` |
| Web | Vercel Hobby, SPA estática, assets y subdominio gratuito `vercel.app` |
| API + Worker lógico | Un único Render Free Web Service Docker en Virginia; ASP.NET Core hospeda API y `BackgroundService` |
| Datos | Supabase Free en `us-east-1` (North Virginia): Auth, PostgreSQL y Storage |
| Integración | Segundo proyecto Supabase Free y servicio Render Free dormible; solo datos sintéticos |

El perfil despliega dos artefactos: SPA y backend. El Worker sigue siendo un componente lógico con cola, lease y heartbeat PostgreSQL, pero en producción corre como `BackgroundService` dentro del proceso ASP.NET para no requerir un servicio de background pagado. `RunningPerformance.Worker` se conserva como host alternativo para local, CI y una futura separación explícitamente aprobada.

Condiciones operativas:

- Render Free dispone de 512 MB RAM y 0.1 CPU, duerme tras 15 minutos sin tráfico entrante y puede tardar cerca de un minuto en reactivarse. La UI muestra un estado de arranque y reintenta con backoff.
- Mientras exista un job pendiente, la SPA consulta su estado por HTTP. Ese tráfico permite procesarlo fuera de la petición original. Si el navegador se cierra o el servicio reinicia/duerme, el lease vence y el job se recupera al siguiente despertar.
- No se envían pings artificiales para impedir el sueño. `/health/live` y `/health/ready` sirven a despliegue y diagnóstico, no a evadir límites gratuitos.
- Las 750 horas gratuitas mensuales de Render se comparten en el workspace. Integración permanece dormida salvo smoke tests y producción puede degradarse o pausarse antes de consumir la cuota.
- Supabase Free permite como referencia 500 MB de base, 1 GB de Storage, 5 GB de egress y dos proyectos. Puede pausar un proyecto de baja actividad; el runbook incluye reactivación.
- La aplicación alerta a 300 MB de base y bloquea nuevas importaciones FIT detalladas a 400 MB; alerta a 700 MB de Storage y bloquea nuevas cargas a 850 MB. Los originales existentes nunca se eliminan automáticamente.
- GitHub Free privado limita Actions a 2,000 minutos y artifacts a 500 MB por mes. CI usa caché acotada, artefactos de retención corta y se detiene al agotar cuota; si el repositorio se hace público, los runners estándar siguen siendo gratuitos.
- Vercel Hobby se usa solo porque esta aplicación es personal y no comercial. Si cambia ese uso, la publicación se pausa hasta aprobar otra alternativa gratuita; no se asciende de plan automáticamente.
- Logs de proveedor, health endpoints y el panel interno cubren observabilidad/alertas. No se introduce un monitor, servicio de correo o telemetría de pago.
- No se registra método de pago ni se aceptan créditos de prueba. Agotar cualquier cuota produce advertencia, degradación, pausa o bloqueo; jamás un upgrade automático.
- Solo se usan subdominios gratuitos de los proveedores. Un dominio personalizado comprado queda fuera de alcance.

Cambiar de proveedor exige otra alternativa de costo obligatorio USD 0 y una decisión explícita. No existe fallback pagado implícito.

### 2.3 Entornos y datos

| Entorno | Datos | Finalidad | Regla |
|---|---|---|---|
| local | sintéticos | desarrollo, Supabase CLI y pruebas | nunca copiar FIT, CSV, rutas o síntomas reales |
| CI | sintéticos efímeros | build, pgTAP, integración y E2E | artefactos revisados y retención corta |
| integration | sintéticos | smoke tests bajo demanda | segundo proyecto Supabase Free y Render dormible; previews Vercel sin backend persistente |
| production | reales privados | fuente de verdad cuando pase `G5` | tiers gratuitos, una cuenta, cuotas vigiladas y backup probado |

La primera importación real ocurre en `APP-006`, después de `G1` y `G2`, y mantiene el CSV/FIT original como respaldo reproducible. La base no se declara fuente única hasta aprobar restauración en `I9`.

## 3. Línea base de versiones

Versiones verificadas y fijadas para iniciar `APP-005`; no se usan versiones preview/canary.

| Capa | Paquete/herramienta | Versión |
|---|---|---:|
| Backend | .NET SDK | `10.0.302` |
| Backend | ASP.NET Core runtime/paquetes Microsoft | `10.0.10` |
| Backend | `Microsoft.AspNetCore.Authentication.JwtBearer` | `10.0.10` |
| Backend | `Microsoft.AspNetCore.OpenApi` / `Microsoft.Extensions.ApiDescription.Server` | `10.0.10` |
| Backend | `Microsoft.OpenApi` | `2.7.5` |
| Backend | `Microsoft.AspNetCore.Mvc.Testing` | `10.0.10` |
| Backend | `Npgsql` | `10.0.3` |
| Backend | `OpenTelemetry.Extensions.Hosting` y familia OTel | `1.17.0` |
| FIT | `Garmin.FIT.Sdk` | `21.205.0` |
| Pruebas .NET | `Microsoft.NET.Test.Sdk` | `18.8.1` |
| Pruebas .NET | `xunit.v3` | `3.2.2` |
| Pruebas .NET | `Testcontainers.PostgreSql` | `4.13.0` |
| Frontend | Node.js | `22.15.1` |
| Frontend | npm | `11.6.2` |
| Frontend | React / React DOM / react-is | `19.2.8` |
| Frontend | TypeScript | `7.0.2` |
| Frontend | Vite | `8.1.5` |
| Frontend | `@vitejs/plugin-react` | `6.0.4` |
| Frontend | `react-router` | `7.18.2` |
| Frontend | `@supabase/supabase-js` | `2.110.8` |
| Frontend | `@tanstack/react-query` | `5.101.4` |
| Frontend | `zod` | `4.4.3` |
| Frontend | `react-hook-form` | `7.82.0` |
| Frontend | Tailwind CSS / `@tailwindcss/vite` | `4.3.3` |
| Frontend | Recharts | `3.10.1` |
| Generación API | `openapi-typescript-codegen` | `0.31.0` |
| Pruebas web | Vitest | `4.1.10` |
| Pruebas web | React Testing Library | `16.3.2` |
| E2E | `@playwright/test` | `1.61.1` |
| Base/local | PostgreSQL | `17` |
| Base/local | Supabase CLI npm package | `2.110.0` |

Antes de crear el lockfile, `I0` debe ejecutar una instalación limpia y comprobar peers entre React, Vite, TypeScript, Tailwind y Recharts. Si una incompatibilidad objetiva impide resolver esta matriz, se documenta y cambia la versión mínima necesaria en un commit dedicado; no se permite resolverla con `--force` o `--legacy-peer-deps`.

## 4. Incrementos verticales

### I0 — Cimientos reproducibles (`APP-005`, parte A)

Entregables:

- estructura `src/`, `tests/`, `supabase/`, `docs/` y `.github/workflows/` definida en APP-003;
- solución y proyectos .NET vacíos con referencias unidireccionales verificadas;
- SPA Vite con router, tokens responsive y página de estado sintética;
- Dockerfile de producción para API + Worker hospedado, Dockerfile/host alternativo para Worker local/CI, `.dockerignore`, archivos de versión y lockfiles;
- OpenAPI generado en build y cliente TypeScript generado sin cambios manuales;
- CI inicial con formato, lint, build, unit tests, secret scan, allowlist de archivos personales y presupuesto de minutos/artefactos GitHub Free;
- `render.yaml` gratuito y configuración Vercel sin dominio comprado, add-ons ni auto-upgrade;
- `.env.example` exclusivamente ficticio.

Salida verificable: un clon limpio construye web/API/Worker y contenedores sin secretos ni acceso a servicios externos.

### I1 — Esquema, RLS y Storage (`APP-005`, parte B)

Entregables:

- migraciones SQL de las 45 tablas, ocho vistas, funciones, constraints, índices y grants;
- roles de capacidad sin login en SQL y logins de entorno creados mediante aprovisionamiento sin versionar contraseñas;
- RLS habilitada y forzada en la misma migración que crea cada tabla privada;
- bucket privado `athlete-files` y políticas por primer segmento `owner_id`;
- seed de dos propietarios y datos sintéticos mínimos;
- pgTAP para aislamiento, inmutabilidad, FKs compuestas, claves parciales y Storage;
- `supabase db reset`, lint y prueba de restauración local automatizados;
- proyecto de integración en `us-east-1` solo después de pasar todo localmente;
- medición de tamaño de base/Storage y guards 300/400 MB y 700/850 MB probados con valores simulados.

Salida verificable: el propietario A no puede leer, enlazar ni modificar ningún objeto del B, incluso con IDs conocidos y conexiones reutilizadas.

### I2 — Acceso, shell, perfil y carreras (`APP-008`)

Entregables:

- Supabase Auth email/contraseña, registro público deshabilitado y cuenta sintética de integración;
- validación JWKS completa y fallback policy autenticada;
- unidad de trabajo Npgsql con `SET LOCAL` y prueba de limpieza del pool;
- login, recuperación, logout y limpieza de caché;
- shell mobile-first a 320/390 px y escritorio, estados de carga/error/vacío;
- perfil, antecedentes, carreras y objetivos versionados como primer corte vertical API–DB–web;
- smoke E2E en Chromium y WebKit.

Salida verificable: ninguna operación acepta `userId`; el propietario proviene solo del token validado.

### I3 — Catálogo visual y plan versionado (`APP-009`)

Entregables:

- ejercicios, revisiones, cero a dos medios, bloques y dosificación;
- assets no personales versionados, licencia, dimensiones y `alt_text`;
- borrador/publicación de plan y una sola versión publicada;
- calendario, sesión del día y guía completa sin depender de imágenes;
- pruebas de inmutabilidad y orden de ejercicios;
- pruebas touch/teclado y sin scroll horizontal en iPhone/PC.

Salida verificable: una sesión sintética de fuerza, movilidad o pliometría se consulta de extremo a extremo y conserva su versión.

### I4 — Importación histórica CSV (`APP-006`)

Entregables:

- streaming a Storage, SHA-256, `source_file`, `ingestion_run` e ítems;
- Worker hospedado en la API con claim por lease, heartbeat, backoff y recuperación de lease vencido;
- validador del contrato normalizado y transacción de publicación completa;
- fixture sintético de 460 filas con nulos, colisiones seguras y modalidades variadas;
- doble importación sin duplicados, errores por fila y conteos reconciliados;
- ensayo en integración; importación privada del CSV real en producción solo después de medir que cabe en las cuotas gratuitas.

Salida verificable: 460 actividades, cero duplicados por clave provisional y ninguna conversión de ausente a cero.

### I5 — FIT incremental y sincronizador (`APP-007`)

Entregables:

- extracción de `Tools/FitProcessor` a `RunningPerformance.Fit`, conservando CLI y contrato determinista;
- upload manual y restringido `fit.upload` por el mismo pipeline;
- pairing de uso único, credencial de 256 bits, expiración, revocación y Credential Manager;
- validación firma/tamaño/CRC/lectura/hash, normalización y escritura por lotes;
- deduplicación ID/hash, enriquecimiento único, cuarentena y precedencia CSV/FIT;
- reproceso atómico con los mismos conteos y sin descargar de nuevo;
- fixtures FIT sintéticos o sanitizados generados, nunca actividades reales en Git.

Salida verificable: repetir un FIT no duplica; un mismo ID con hash diferente conserva ambos orígenes y abre cuarentena.

### I6 — Planificado, realizado y captura (`APP-010`)

Entregables:

- propuesta/confirmación/retiro de vínculos sin borrar actividad;
- una sesión lógica puede agrupar varias actividades Garmin;
- cinco estados TRN-003 y resultado aun sin actividad;
- check-ins inmediato/24 h/48 h con RPE y P5 por componentes;
- sRPE calculado desde duración total de sesión lógica;
- UI adaptada a uso rápido después del entrenamiento.

Salida verificable: una sesión sintética dividida en dos FIT cuenta una vez en cumplimiento y sRPE.

### I7 — Evaluación, decisión y ajuste (`APP-011`)

Entregables:

- snapshots P1–P5 provisionales/finales con fórmula, versión y evidencia navegable;
- separación treadmill/outdoor y ritmo agregado tiempo/distancia;
- precedencia verde/amarilla/roja sin score compuesto P5;
- decisión humana confirmada y ajuste que crea una nueva versión de plan;
- observación, evidencia, comparación, interpretación y recomendación auditadas;
- pruebas de que una propuesta automática no publica cambios.

Salida verificable: se cierra una semana sintética completa y cada agregado conduce a sus fuentes.

### I8 — Dashboard, exportación y ciclo de vida (`APP-012`)

Entregables:

- dashboard actual y tendencias 4/8/12 semanas mediante agregados del servidor;
- gráficas accesibles con alternativa tabular, carga por ruta y modalidades separadas;
- siguiente sesión, carga, recuperación y alertas pendientes;
- exportación versionada, job, objeto privado temporal y expiración;
- solicitudes explícitas de archivo/eliminación sin saltarse auditoría;
- límites de consultas y revisión de `activity_samples`.
- guardas internas de consumo gratuito de base, Storage, egress, CI y horas de backend, con alertas preventivas fuera del dashboard cotidiano del atleta.

Salida verificable: el atleta ve acciones prácticas de entrenamiento, puede recorrer su historial y descargar sus datos con autorización y vencimiento, sin URL pública; las operaciones administrativas quedan en Perfil.

### I9 — Endurecimiento y producción (`APP-013`)

Entregables:

- GitHub Free, Vercel Hobby, Render Free y Supabase Free reproducibles desde configuración documentada;
- CORS/CSP/HSTS, rate limits, tamaños de archivo y OpenAPI de producción cerrado;
- logs/trazas/métricas sin datos sensibles, heartbeat Worker, arranque frío y alertas de cuota;
- backup lógico con `supabase db dump`, exportación cifrada de Storage fuera de Git/servicios publicados, restauración probada y runbook de incidentes/rollback;
- auditoría de dependencias, contenedores, secretos y contenido completo del primer commit;
- E2E del criterio MVP, smoke real en Safari de iPhone y Chrome/Edge de PC;
- manifest e iconos servidos por HTTPS, instalación mediante “Añadir a pantalla de inicio” y apertura standalone verificadas en un iPhone real; operación offline no requerida;
- piloto de arranque frío, suspensión, recuperación y cuotas de siete días; costo obligatorio confirmado en USD 0 y decisión de fuente de verdad.

Salida verificable: se cumplen los 16 criterios APP-001, el despliegue completo no exige ningún pago y existe rollback sin pérdida de originales.

## 5. Orden de migraciones

| Serie | Contenido | Regla de salida |
|---|---|---|
| `0000` | extensiones, esquema `app`, helpers, roles de capacidad | sin secretos ni privilegios de dominio a `anon` |
| `0010` | perfil, salud, carreras y objetivos | RLS/FORCE y FK compuestas en el mismo cambio |
| `0020` | ejercicios, revisiones, medios, planes y sesiones | una publicada; revisiones usadas inmutables |
| `0030` | objetos, archivos, jobs, actividades y procedencia | identidades parciales y no overwrite por `NULL` |
| `0040` | intentos FIT, warnings, esquemas, sesiones, laps, eventos, zonas y muestras | intento vigente y swap transaccional |
| `0050` | vínculos, outcomes y check-ins | un vínculo activo; ausentes como `NULL` |
| `0060` | evaluaciones, métricas/evidencia, decisiones, ajustes, notas y auditoría | P5 separado y eventos append-only |
| `0070` | exportaciones, lifecycle, vistas, funciones y seeds controlados | sin acceso directo de navegador al esquema `app` |
| `0080` | bucket/políticas Storage, grants finales y pruebas de privilegios | dos propietarios y sync client negativos |

Cada archivo es transaccional cuando PostgreSQL lo permite. Una tabla privada nunca queda desplegada temporalmente sin RLS, `FORCE ROW LEVEL SECURITY`, FK compuesta y prueba negativa.

## 6. Puertas obligatorias

### G0 — Reproducibilidad

- clon limpio, installs bloqueados, builds y contenedores;
- ninguna credencial/dato real; SBOM y auditoría sin vulnerabilidad crítica conocida;
- OpenAPI y cliente generado sin diff no explicado.

### G1 — Aislamiento de datos

- `supabase db reset`, lint y pgTAP verdes;
- RLS A→B negativa en las 45 tablas privadas y Storage;
- roles sin `BYPASSRLS`, contexto local limpiado al reciclar conexiones;
- backup/restore local y migración desde cero reproducibles.

### G2 — Identidad y superficie web

- JWT completo, registro público cerrado, ningún `userId` confiado;
- CSP/CORS y logs sin token o body;
- flujos de acceso y primer corte vertical pasan Chromium/WebKit.

### G3 — Ingestión

- CSV doble con 460/460, FIT repetido y conflicto ID/hash;
- job reiniciable, leases vencidos recuperados y commits atómicos;
- originales privados, hashes y procedencia reconciliados.

### G4 — Lógica deportiva

- P1–P5, sRPE, varias actividades por sesión y modalidades probados;
- semáforo de peor señal y decisión humana;
- plan publicado inmutable y ajuste versionado.

### G5 — Producción

- 16 criterios MVP y flujos reales iPhone/PC, incluida la instalación y apertura desde la pantalla de inicio;
- restauración manual, alertas, rollback, rotación de secretos, suspensión y arranque frío ensayados;
- siete días en USD 0, dentro de todas las cuotas y sin job perdido después de dormir/reiniciar;
- verificación de que no hay método de pago, prueba temporal, add-on, dominio comprado ni auto-upgrade;
- revisión del primer commit de `App/` y autorización explícita antes de publicar.

## 7. Definición de terminado por incremento

Un incremento solo cierra cuando:

1. entrega un flujo observable desde UI o cliente hasta persistencia, salvo los cimientos `I0–I1`;
2. incluye camino feliz, validaciones y pruebas negativas de propietario;
3. conserva unidades, `NULL`, timestamps y procedencia según contrato;
4. actualiza OpenAPI, cliente generado, migraciones y documentación;
5. funciona con fixtures sintéticos en CI y no introduce datos personales;
6. contempla loading/empty/error/pending/quarantine cuando aplique;
7. pasa accesibilidad, 320/390 px y escritorio para toda UI nueva;
8. deja logs y métricas operables sin payload sensible;
9. no deja migraciones, flags o rutas provisionales sin dueño y criterio de retiro.

## 8. Backlog operativo

Los IDs `APP-006` y `APP-007` ya existían antes de este plan. Su numeración se conserva, aunque el grafo de dependencias coloca `APP-008` y `APP-009` antes de `APP-006`.

| Orden | Tarea | Incremento | Tamaño relativo | Dependencia |
|---:|---|---|---|---|
| 1 | `APP-005` | I0–I1 | XL | `APP-004` |
| 2 | `APP-008` | I2 | L | `APP-005` |
| 3 | `APP-009` | I3 | L | `APP-008` |
| 4 | `APP-006` | I4 | L | `APP-005`, `APP-009`, `GAR-004` |
| 5 | `APP-007` | I5 | XL | `APP-006`, `GAR-008` |
| 6 | `APP-010` | I6 | L | `APP-007`, `APP-009` |
| 7 | `APP-011` | I7 | L | `APP-010` |
| 8 | `APP-012` | I8 | L | `APP-011` |
| 9 | `APP-013` | I9 | L | `APP-012` |

No se asignan fechas sin conocer capacidad real. Cada tarea puede dividirse si un incremento no cabe en un cambio revisable, pero no se salta su puerta de salida.

## 9. Riesgos y disparadores de revisión

| Riesgo | Control | Revisar cuando |
|---|---|---|
| Render duerme y corta trabajo en proceso | job persistente, lease vencible, polling visible y recuperación al despertar | un job se pierde o queda atascado tras suspensión/reinicio |
| CPU/RAM gratuitas limitan FIT grandes | streaming, lotes acotados, límites de upload y medición con 512 MB/0.1 CPU | OOM, timeout o latencia inaceptable con fixture máximo |
| Cuotas gratuitas se agotan | dashboard, umbrales preventivos y bloqueo sin billing | base 300/400 MB, Storage 700/850 MB, CI o 750 h se acercan al límite |
| Supabase Free pausa y no ofrece backup descargable automático | reactivación documentada, dump/export cifrado local y restore ensayado | proyecto pausado o backup manual incumplido |
| RLS contextual con pooling filtra identidad | `SET LOCAL`, transacción obligatoria y test de reutilización | cualquier consulta ocurre fuera de unidad de trabajo |
| 45 tablas en un solo hito | series de migración y pgTAP por grupo | `db reset` deja de ser rápido/reproducible |
| volumen de muestras FIT | COPY, índice por actividad/orden, medición y bloqueo preventivo | 300 MB de base o proyección hacia 400 MB |
| deriva de contrato OpenAPI | generación determinista y diff CI | DTO manual o cliente editado |
| paquete reciente incompatible | lock exacto y smoke de peers en `I0` | requiere `--force`, preview o excepción de auditoría |
| dato real llega a Git/CI | allowlist, secret/PII scan y revisión de primer commit | cualquier FIT/CSV/ruta/hash no sintético detectado |

## 10. Fuentes oficiales verificadas

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [NuGet: JwtBearer 10.0.10](https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.JwtBearer/10.0.10)
- [NuGet: Npgsql 10.0.3](https://www.nuget.org/packages/Npgsql/10.0.3)
- [NuGet: OpenTelemetry.Extensions.Hosting 1.17.0](https://www.nuget.org/packages/OpenTelemetry.Extensions.Hosting/1.17.0)
- [NuGet: Garmin FIT SDK 21.205.0](https://www.nuget.org/packages/Garmin.FIT.Sdk/21.205.0)
- [npm: React](https://www.npmjs.com/package/react)
- [npm: Vite](https://www.npmjs.com/package/vite)
- [npm: Supabase JS](https://www.npmjs.com/package/@supabase/supabase-js)
- [npm: Supabase CLI](https://www.npmjs.com/package/supabase)
- [GitHub Actions billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions)
- [Vercel Hobby](https://vercel.com/docs/plans/hobby)
- [Render Free](https://render.com/docs/free)
- [Render compute plans](https://render.com/docs/compute-plans)
- [Render regions](https://render.com/docs/regions)
- [Render health checks](https://render.com/docs/health-checks)
- [Render Docker](https://render.com/docs/docker)
- [Supabase pricing](https://supabase.com/pricing)
- [Supabase Free project pausing](https://supabase.com/docs/guides/platform/free-project-pausing)
- [Supabase database size](https://supabase.com/docs/guides/platform/database-size)
- [Supabase backups](https://supabase.com/docs/guides/platform/backups)
- [Supabase Postgres 17 upgrade notes](https://supabase.com/docs/guides/platform/upgrading)

## 11. Traspaso a APP-005

`APP-005` debe ejecutar primero `I0` y la parte local de `I1`. Solo después de `G0` y de todas las pruebas RLS locales puede crear el proyecto Supabase Free de integración en `us-east-1`. No debe crear producción, importar `Data/activities-normalized.csv`, utilizar FIT reales ni habilitar ningún elemento pagado. Debe demostrar desde el primer incremento que el backend combinado construye en el perfil de 512 MB/0.1 CPU y que agotar una cuota simulada bloquea la operación sin activar cobros.
