---
name: verify
description: Ejecuta la tubería de verificación de Running Performance (build backend, tests, lint/tests web, pgTAP y puertas de publicación PowerShell). Úsala al cerrar una tarea, antes de commitear, o cuando pidan "verificar", "correr las pruebas" o "revisar que no rompí nada". Selecciona automáticamente el alcance mínimo según qué archivos cambiaron.
---

# Verificación de Running Performance

Este repo no tiene CI: `CONTRIBUTING.md` exige que todas las puertas se ejecuten localmente. La política del proyecto es correr **solo las pruebas afectadas por el cambio** durante el desarrollo, y la suite completa únicamente antes de una entrega o despliegue.

## 1. Determinar el alcance

```bash
git status --short && git diff --stat HEAD
```

Ignora los archivos bajo `src/web/src/api/generated/` que aparezcan modificados sin contenido real (`git diff --numstat` vacío): es solo CRLF vs LF en Windows.

Elige el alcance según lo que cambió:

| Cambió | Ejecuta |
|---|---|
| Solo `src/web/src/` (React/TS) | Bloque **Web** |
| Solo `src/backend/` | Bloque **Backend** |
| Endpoints C# (records de request/response) | **Backend** + **Web** (el contrato se regenera en cadena) |
| `supabase/migrations/` | **Base de datos** + **Backend** |
| `scripts/`, `render.yaml`, `Dockerfile`, `vercel.json` | **Puertas de publicación** |
| Entrega, despliegue o duda | **Todo**, en el orden de abajo |

## 2. npm pineado

El lockfile exige npm `11.6.2`. Si `npm --version` no lo reporta, antepón `corepack` en cada comando npm:

```bash
corepack npm@11.6.2 --prefix src/web <script>
```

`corepack enable` requiere admin en esta máquina; invocar `corepack npm@11.6.2` directamente no.

## 3. Bloques

Ejecuta desde la raíz del repo. Detente en el primer fallo y repórtalo con su salida en vez de seguir.

### Backend

```bash
dotnet restore RunningPerformance.slnx --locked-mode
dotnet build RunningPerformance.slnx --configuration Release --no-restore
dotnet test RunningPerformance.slnx --configuration Release --no-build
```

`dotnet build` regenera `src/web/openapi/running-performance.json`. Si ese archivo cambió de verdad, el bloque **Web** deja de ser opcional.

Los tests de integración (`tests/backend-integration/`) necesitan Supabase local corriendo.

### Web

```bash
npm --prefix src/web run lint    # generate:api + tsc -b; no hay ESLint
npm --prefix src/web test
```

Añade `npm --prefix src/web run build` antes de un despliegue, y `npm --prefix src/web run test:e2e` (Playwright, Chromium + WebKit) si tocaste un flujo visible.

### Base de datos

Requiere Docker y Supabase local (`npm --prefix src/web run db:start`).

```bash
npm --prefix src/web run db:lint
npm --prefix src/web run db:test
```

`db:reset` reconstruye la base desde migraciones + seed: es destructivo y solo válido con datos sintéticos. Si Kong falla tras reiniciar Docker, `npm --prefix src/web run db:repair`.

### Puertas de publicación

PowerShell, no bash:

```powershell
pwsh ./scripts/Test-SchemaContract.ps1
pwsh ./scripts/Test-PublishBoundary.ps1 -GitIndex
pwsh ./scripts/Test-FreeDeployment.ps1
pwsh ./scripts/Test-ProductionHardening.ps1
pwsh ./scripts/Test-BackupCrypto.ps1
```

`Test-PublishBoundary.ps1 -GitIndex` es la que impide publicar datos privados de salud o entrenamiento. Córrela siempre antes de commitear, aunque el cambio parezca inocuo.

Las dos variantes miran cosas distintas: con `-GitIndex` solo revisa archivos rastreados por git (la que exigen README y CONTRIBUTING antes de publicar); sin el flag recorre todo el filesystem y falla ante cualquier `.env*` que no sea `.env.example`, aunque esté ignorado. Si la variante sin flag falla por un `.env` local, la respuesta correcta es borrarlo y exportar las variables en la shell, no relajar el script.

## 4. Reportar

Di qué bloques corriste, cuáles omitiste y por qué. Cifras concretas (`39 unit + 4 integration`, `40 web`), no "todo pasó". Si omitiste un bloque porque faltaba una dependencia del entorno (Docker apagado, Supabase sin arrancar), dilo explícitamente — un bloque omitido no es un bloque verde.
