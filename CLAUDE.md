# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Idioma

El producto, la documentación y **todo el texto visible en la UI están en español**. Los mensajes de error de la API que llegan al usuario también (ver `SessionCompletionEndpoints.cs`). El código (identificadores, comentarios, nombres de tablas) está en inglés. Mantén esa separación.

## Toolchain fijado

.NET SDK `10.0.400` (`global.json`), Node `22.15.1` (`.nvmrc`), npm `11.6.2` (`packageManager`), Docker Desktop.

**El npm pineado importa.** El lockfile fue generado con npm 11.6.2; con otras versiones `npm ci` falla por `@tailwindcss/oxide-wasm32-wasi` (paquete opcional `cpu: wasm32` cuyos `@emnapi/*` son `bundleDependencies`). Si tu npm global es otro, usa el pineado sin instalarlo:

```bash
corepack npm@11.6.2 --prefix src/web ci
```

No "arregles" ese fallo con `npm install`: reescribe el lockfile, y `CONTRIBUTING.md` exige conservarlo.

**Si `db:start` falla con `No matching Supabase CLI binary package found for win32-x64`:** falta `@supabase/cli-windows-x64`, una dependencia *opcional* cuya descarga npm no considera fatal, así que un `npm ci` puede terminar "exitoso" sin ella. Está en el lockfile con `os`/`cpu` correctos — basta repetir `npm ci` (`corepack npm@11.6.2 ci` desde `src/web`). Comprueba con `ls src/web/node_modules/@supabase/cli-windows-x64/bin/`. Ojo: el proceso puede salir con código 0 pese al error, así que verifica `docker ps` en vez de confiar en el código de salida.

**No crees un `.env` en la raíz del repo.** Nada lo lee — Vite toma `import.meta.env` desde `src/web`, y la API lee variables de entorno del proceso — y `Test-PublishBoundary.ps1` (sin `-GitIndex`) falla ante cualquier `.env*` que no sea `.env.example`, aunque esté en `.gitignore`. `.env.example` sirve de referencia de valores; exporta las variables en la shell como documenta el README.

## Comandos

```powershell
# Instalar
npm ci --prefix src/web
dotnet restore RunningPerformance.slnx --locked-mode

# Base de datos local (requiere Docker)
npm --prefix src/web run db:start     # Supabase local
npm --prefix src/web run db:reset     # reconstruye desde migraciones + seed (destructivo)
npm --prefix src/web run db:repair    # stop+start si Kong falla tras reiniciar Docker
npm --prefix src/web run db:lint
npm --prefix src/web run db:test      # pgTAP: supabase/tests/database/

# Backend
dotnet build RunningPerformance.slnx --configuration Release --no-restore
dotnet test RunningPerformance.slnx --configuration Release --no-build
dotnet test tests/backend-unit --filter "FullyQualifiedName~WeeklyEvaluationRules"   # una sola clase

# Frontend
npm --prefix src/web run lint    # generate:api + tsc -b (no hay ESLint)
npm --prefix src/web test        # vitest run src
npm --prefix src/web test -- src/lib/coach.test.ts   # un solo archivo
npm --prefix src/web run build
npm --prefix src/web run test:e2e                     # Playwright (Chromium + WebKit)
```

Puertas de publicación (PowerShell, todas locales — **no hay GitHub Actions**):

```powershell
pwsh ./scripts/Test-SchemaContract.ps1
pwsh ./scripts/Test-PublishBoundary.ps1 -GitIndex
pwsh ./scripts/Test-FreeDeployment.ps1
pwsh ./scripts/Test-ProductionHardening.ps1
pwsh ./scripts/Test-BackupCrypto.ps1
```

Durante el desarrollo se ejecutan solo las pruebas afectadas por el cambio; la suite completa se reserva para entrega o despliegue.

### Levantar la app completa

Tres piezas: Supabase local, la API y Vite. Después de `db:start`, en una terminal:

```powershell
$env:ASPNETCORE_URLS='http://127.0.0.1:5080'
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run --project src/backend/RunningPerformance.Api --configuration Release
```

En otra (toma la clave pública de la instancia local automáticamente):

```powershell
$status = .\src\web\node_modules\.bin\supabase.cmd --workdir . status -o env
$line = ($status | Select-String '^(PUBLISHABLE_KEY|ANON_KEY)=' | Select-Object -First 1).Line
$env:VITE_SUPABASE_PUBLISHABLE_KEY = ($line -split '=', 2)[1].Trim('"')
$env:VITE_SUPABASE_URL='http://127.0.0.1:54321'
$env:VITE_API_BASE_URL='http://127.0.0.1:5080'
npm --prefix src/web run dev -- --host 127.0.0.1
```

App en `http://127.0.0.1:5173`, Studio en `54323`, PostgreSQL en `54322` (`postgres`/`postgres`). Cuenta sintética A: `athlete-a@example.invalid` / `synthetic-only-a`.

## Arquitectura

### Cadena de contratos generados

```
Endpoints C# (records)  →  src/web/openapi/running-performance.json  →  src/web/src/api/generated/
      (dotnet build)                                    (npm run generate:api)
```

Ambos extremos son **artefactos generados y versionados**. `dotnet build` reescribe el JSON de OpenAPI; `npm run lint`/`build` reescribe el cliente TypeScript. Nunca edites `src/web/src/api/generated/` ni `src/web/openapi/` a mano.

**Para agregar un campo a una respuesta de API:** modifica el `record` en el archivo de endpoints C# → `dotnet build` → `npm --prefix src/web run lint` → consúmelo desde React.

Nota de ruido: en Windows el generador escribe CRLF y `.gitattributes` pide LF, así que tras `lint`/`build` los archivos de `api/generated/` aparecen como modificados en `git status` aunque `git diff --numstat` salga vacío. Es cosmético; git normaliza al commitear.

### Backend

.NET 10 minimal APIs, **sin ORM** — SQL crudo con Npgsql. `src/backend/`:

- `RunningPerformance.Api` — un archivo `*Endpoints.cs` por área, cada uno con su `Map*Endpoints()` extension registrada en `Program.cs`. Los DTOs (`record`s) viven al final del mismo archivo, no en un proyecto aparte.
- `RunningPerformance.Application` — reglas puras y testeables sin BD (`WeeklyEvaluationRules`, `DashboardRules`, validadores CSV, `FreeTierQuotaGuard`).
- `RunningPerformance.Infrastructure` — acceso a datos, colas de ingestión, Storage, telemetría.
- `RunningPerformance.Fit` — parser de archivos FIT (Garmin SDK).
- `RunningPerformance.Worker` — worker de ingestión. **En producción corre dentro del mismo contenedor que la API**; el proyecto separado es para pruebas locales.

### Aislamiento por propietario (patrón obligatorio)

Todo acceso a datos pasa por `OwnerDataSource.OpenAsync(ownerId)`, que abre conexión + transacción y ejecuta `set local role rp_api` más `set_config('request.jwt.claim.sub', ownerId)`. Las políticas RLS (`app.owns(owner_id)`, forzadas en todas las tablas) leen ese claim. El patrón en cada endpoint es:

```csharp
var ownerId = principal.GetRequiredOwnerId();
await using var session = await dataSource.OpenAsync(ownerId, cancellationToken);
// ... comandos con command.Transaction = session.Transaction
await session.CommitAsync(cancellationToken);
```

`OwnerDbSession` hace rollback si no llamas a `CommitAsync`. Nunca abras una `NpgsqlConnection` directa desde un endpoint: se salta RLS.

### Base de datos

Migraciones SQL numeradas en `supabase/migrations/` (`0000`–`0160`), esquema `app`, ~45 tablas. No hay migraciones generadas por código. Tests pgTAP en `supabase/tests/database/`.

Lógica que vive **en la BD, no en C#** — respétala en vez de reimplementarla:

| Función | Qué hace |
|---|---|
| `app.clone_training_plan_draft` | Crea el borrador desde la versión publicada. Falla si ya existe un borrador (**solo uno a la vez, y no hay función para descartarlo**). |
| `app.publish_training_plan_version` | Promueve el borrador a publicado. |
| `app.reject_published_plan_content_change` | Trigger sobre `planned_sessions`/`planned_session_blocks`/`planned_session_exercises`: `"Published plan content is immutable"`. Para cambiar contenido del plan hay que clonar borrador → editar → publicar. |
| `app.create_weekly_evaluation_snapshot` | Snapshot semanal inmutable P1–P5. Se dispara por acción del usuario (`POST /snapshots`), no hay job automático. |
| `app.build_athlete_export` | Exportación de datos del atleta. |

Las decisiones semanales (`app.weekly_decisions`) son **append-only**: confirmada una, no se puede editar ni reemplazar.

### Frontend

React 19 + Vite + react-router (rutas en `App.tsx`, todas lazy y bajo `ProtectedRoute` + `AppShell`), TanStack Query para servidor, Tailwind 4. Sin librería de estado global.

- `src/web/src/pages/` — una página por ruta; contienen bastante JSX inline denso en una sola línea, ese es el estilo del repo.
- `src/web/src/lib/` — lógica pura y testeada con vitest (`coach.ts`, `dashboard.ts`, `weekAgenda.ts`, `calendar.ts`). **Aquí va la lógica nueva que se pueda testear sin renderizar.**

`src/web/src/lib/coach.ts` (`buildCoachReview`, `classifyRunner`) es un **motor de reglas determinista en TypeScript, sin ninguna llamada a IA**, pese a aparecer en la UI como "Coach IA". Sus umbrales están hardcodeados.

### Modelo de dominio (conceptos que cruzan varios archivos)

- **`app.activities` es una sola tabla** para running y fuerza, distinguidas por `activity_category`/`modality`. No hay tablas separadas.
- **Planificado vs. realizado**: `activities` ←→ `planned_sessions` vía `activity_session_links` (`proposed`/`confirmed`/`withdrawn`/`rejected`; solo un vínculo activo por actividad). Varias actividades vinculadas a una misma sesión planeada forman una **"sesión lógica"** (ver la vista `app.v_logical_session_srpe`).
- **Ejercicios de fuerza**: `planned_sessions` → `planned_session_blocks` → `planned_session_exercises` → `exercise_revisions` (técnica, imágenes, cues de seguridad). El componente `PlannedExercise` en `PlanPage.tsx` ya los renderiza inline. El campo `planned_sessions.main_set` es una **alternativa de texto libre** a esos bloques estructurados; algunas sesiones usan uno y no el otro.
- **Asimetría a tener presente**: `GET /api/v1/activities/{id}` devuelve la actividad y su procedencia, pero **no** la sesión planeada. La relación se expone por el otro lado, en `GET /api/v1/sessions/{id}/completion` (indexado por sesión planeada).
- **Ajustes de plan**: `PlanVersionAdjustmentRequest`/`PlannedSessionAdjustmentRequest` solo permiten cambiar `ScheduledDate` y `Objective` — no volumen, ritmo, RPE ni bloques.

## Restricciones del proyecto

**Costo obligatorio USD 0.** GitHub Free (solo repo, Actions deshabilitado), Vercel Hobby (SPA), un Render Free Web Service (API + Worker en un contenedor), Supabase Free. Nada de tarjeta, add-ons, dominio comprado ni auto-upgrade. `FreeTierQuotaGuard` vigila las cuotas y bloquea antes de generar cobros; esas guardas no se le muestran al atleta.

**Límite publicable.** El repo es público y la app procesa datos privados de salud. Nunca deben entrar: archivos FIT/TCX/GPX reales, CSV/JSON de actividades personales, rutas GPS, capturas de Garmin, estados autenticados del navegador, credenciales o `.env`, dumps de Supabase. Usa `example.invalid` y credenciales marcadas como sintéticas. No registres cuerpos, query strings, cabeceras de autorización ni datos del atleta en logs ni telemetría. `Test-PublishBoundary.ps1` verifica esto.

Los datos ausentes se conservan como `NULL`/`ND`; no se inventan ni se imputan.

## Documentación

`docs/` tiene especificaciones numeradas `app-005`…`app-013`, cada una en `.md` (narrativa) y `.json` (contrato estructurado), más `architecture.md`, `data-model.md`, `functional-requirements.md`, `coach-method-v1.md` y `operations-runbook.md`. Consúltalas antes de cambiar comportamiento que ya esté especificado.
