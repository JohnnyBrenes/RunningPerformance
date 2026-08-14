# Running Performance App

Aplicación personal para planificar, importar, analizar y ajustar entrenamiento de running mediante React, C#/.NET y Supabase PostgreSQL.

La interfaz será mobile-first y estará verificada tanto en Safari para iPhone como en Chrome/Edge para PC. La publicación HTTPS puede añadirse a la pantalla de inicio del iPhone y abrirse en modo independiente.

## Estado

`APP-006` a `APP-012` entregan importación histórica CSV, ingestión FIT incremental, acceso y shell responsive, perfil/contexto de salud, carreras con metas versionadas, catálogo técnico, plan semanal inmutable, relación planificado–realizado, evaluación P1–P5, dashboard práctico, historial navegable y gestión privada de datos. `APP-013` está en aceptación de producción: el endurecimiento y las puertas locales están implementados, mientras que el despliegue, la restauración aislada, la verificación física en iPhone y el piloto de siete días permanecen pendientes. El perfil elige automáticamente ilustraciones masculinas o femeninas sin cambiar la prescripción. El esquema nace desde migraciones SQL con 45 tablas, nueve vistas, RLS forzada, Storage privado, dos cuentas sintéticas y guardas internas de cuota. La API y el Worker lógico comparten un único contenedor de producción; el proyecto Worker independiente queda disponible para pruebas locales/CI y evolución futura.

La publicación completa debe conservar costo obligatorio USD 0: GitHub Free para repositorio/CI, Vercel Hobby para la SPA, un Render Free Web Service para API + Worker hospedado y Supabase Free para Auth/PostgreSQL/Storage. No se permiten tarjeta, pruebas temporales, add-ons, dominio comprado ni auto-upgrade; las cuotas se vigilan y bloquean antes de generar cobros.

## Acceso desde la pantalla de inicio del iPhone

El frontend publica `/manifest.webmanifest`, iconos versionados y los metadatos de Safari necesarios. Después del despliegue HTTPS:

1. Abre la aplicación en Safari en el iPhone.
2. Toca **Compartir**.
3. Elige **Añadir a pantalla de inicio** y confirma **Añadir**.

El acceso se abre con el nombre e icono de Running Performance y en modo independiente. Esta instalación no habilita operación offline: la conexión sigue siendo necesaria para autenticación, consultas y cambios. En **Perfil → Acceso rápido** la aplicación muestra estas instrucciones y confirma cuando se ejecuta en modo standalone.

Si cambia la marca, regenera los PNG versionados con `pwsh ./scripts/Generate-WebAppIcons.ps1`, incrementa el sufijo de los archivos y actualiza el manifest y `index.html` para evitar iconos obsoletos en caché.

## Límite publicable

`App/` es el único directorio de este workspace preparado para convertirse eventualmente en repositorio GitHub. La raíz contiene datos privados de entrenamiento y no debe inicializarse ni publicarse como repositorio.

El repositorio futuro puede contener:

- código fuente;
- migraciones y políticas de seguridad;
- pruebas;
- documentación técnica sanitizada;
- configuración de ejemplo sin secretos;
- datos sintéticos mínimos.
- ilustraciones de ejercicios propias, generadas o con licencia compatible y sin datos personales.

Nunca debe contener:

- archivos FIT/TCX/GPX reales;
- CSV o JSON con actividades personales;
- rutas GPS, capturas de Garmin o fotos privadas;
- estados autenticados del navegador;
- credenciales, tokens, claves o archivos `.env`;
- dumps de Supabase;
- informes deportivos identificables del workspace privado.

Los requerimientos están en [docs/functional-requirements.md](docs/functional-requirements.md). La evaluación del prototipo anterior está en [docs/legacy-repository-assessment.md](docs/legacy-repository-assessment.md).
El modelo de datos está en [docs/data-model.md](docs/data-model.md), con contrato estructurado en [docs/data-model.json](docs/data-model.json).
La arquitectura está en [docs/architecture.md](docs/architecture.md), con contrato estructurado en [docs/architecture.json](docs/architecture.json).
El plan ejecutable está en [docs/implementation-plan.md](docs/implementation-plan.md), con contrato estructurado en [docs/implementation-plan.json](docs/implementation-plan.json).

## Entorno local

Requisitos fijados: .NET SDK `10.0.400`, Node `22.15.1`, npm `11.6.2` y Docker Desktop.

```powershell
npm ci --prefix src/web
npm --prefix src/web run db:start
npm --prefix src/web run db:reset
npm --prefix src/web run db:lint
npm --prefix src/web run db:test
pwsh ./scripts/Test-LocalStorageIsolation.ps1
dotnet restore RunningPerformance.slnx --locked-mode
dotnet build RunningPerformance.slnx --configuration Release --no-restore
dotnet test RunningPerformance.slnx --configuration Release --no-build
npm --prefix src/web run lint
npm --prefix src/web test
npm --prefix src/web run test:e2e
npm --prefix src/web run build
docker build --tag running-performance-api:local .
```

Supabase expone API en `http://127.0.0.1:54321`, PostgreSQL en `127.0.0.1:54322` y Studio en `http://127.0.0.1:54323`. Docker Desktop muestra cinco componentes funcionales (`db`, `auth`, `storage`, `rest`, `kong`) y dos herramientas locales (`studio`, `pg_meta`). Edge Runtime, Realtime, Analytics, correo, Image Proxy y Pooler están desactivados para este incremento.

DBeaver puede conectarse directamente a la base local con host `127.0.0.1`, puerto `54322`, base `postgres`, usuario `postgres`, contraseña `postgres` y SSL desactivado. Los datos de dominio están en el esquema `app`; `auth` y `storage` pertenecen a Supabase. Esa sesión administrativa evita RLS, por lo que sirve para diagnóstico, no para demostrar aislamiento entre propietarios.

Para ver la aplicación contra toda la pila local, abre dos terminales después de iniciar Supabase. En la primera:

```powershell
$env:ASPNETCORE_URLS='http://127.0.0.1:5080'
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run --project src/backend/RunningPerformance.Api --configuration Release
```

En la segunda, toma automáticamente la clave pública de la instancia local y levanta Vite:

```powershell
$status = .\src\web\node_modules\.bin\supabase.cmd --workdir . status -o env
$line = ($status | Select-String '^(PUBLISHABLE_KEY|ANON_KEY)=' | Select-Object -First 1).Line
$env:VITE_SUPABASE_PUBLISHABLE_KEY = ($line -split '=', 2)[1].Trim('"')
$env:VITE_SUPABASE_URL='http://127.0.0.1:54321'
$env:VITE_API_BASE_URL='http://127.0.0.1:5080'
npm --prefix src/web run dev -- --host 127.0.0.1
```

Abre `http://127.0.0.1:5173`. La cuenta sintética A es `athlete-a@example.invalid` / `synthetic-only-a`; nunca se reutiliza en producción.

Si Docker Desktop se reinicia y Kong falla porque sus montajes temporales quedaron obsoletos, ejecuta `npm --prefix src/web run db:repair`. Este ciclo detiene y vuelve a iniciar Supabase conservando el volumen local; `db:reset` sí reconstruye la base y debe reservarse para datos sintéticos.

Para ejecutar el contenedor combinado contra PostgreSQL local:

```powershell
docker run --rm --name running-performance-api --memory 512m --cpus 0.1 --publish 8080:8080 --env 'DATABASE_URL=Host=host.docker.internal;Port=54322;Database=postgres;Username=postgres;Password=postgres' running-performance-api:local
```

Los endpoints de operación son `/health/live`, `/health/ready` y `/api/v1/status`. La superficie autenticada incluye `/api/v1/profile`, `/api/v1/races`, `/api/v1/exercises`, `/api/v1/plans`, `/api/v1/activities` y `/api/v1/ingestion-runs`. Todo el entorno local contiene exclusivamente datos sintéticos; `.env`, dumps y datos del atleta están excluidos.

## Importación histórica CSV

La API recibe `text/csv` por stream, calcula SHA-256 mientras escribe un temporal acotado, guarda el original en `athlete-files` sin upsert y devuelve `202` con el trabajo persistido. El Worker reclama con lease, valida exactamente el contrato normalizado de 57 columnas y publica las 460 filas en una transacción completa. Repetir el mismo archivo reutiliza el objeto por hash, conserva una ejecución y observaciones nuevas, y reconcilia contra `provisional_activity_key` sin duplicar actividades.

Para ensayar sin datos reales:

```powershell
pwsh ./scripts/New-SyntheticHistoricalCsv.ps1 -OutputPath "$env:TEMP/rp-activities-synthetic.csv"
pwsh ./scripts/Import-HistoricalActivities.ps1 `
  -CsvPath "$env:TEMP/rp-activities-synthetic.csv" `
  -AccessToken '<JWT de la cuenta sintética>'
```

La importación real usa el mismo comando desde fuera de `App/`; el CSV nunca se copia al repositorio. La producción requiere `SUPABASE_SECRET_KEY` y sólo se importa tras confirmar que el archivo cabe en los umbrales gratuitos. Toda prueba local/CI usa exclusivamente fixtures sintéticos.

## Ingestión incremental FIT

La carga manual autenticada y el sincronizador local convergen en el mismo pipeline: staging acotado, firma FIT, SHA-256, objeto privado, recibo, job con lease, validación completa mediante Garmin FIT SDK, normalización y publicación atómica por lotes. El ID Garmin siempre viene del contexto de descarga. Un reintento con el mismo `Idempotency-Key` reutiliza el recibo; un ID existente con otro hash conserva el segundo origen y lo pone en cuarentena.

El emparejamiento crea un token de uso único y entrega una credencial revocable con alcance exclusivo `fit.upload`. En Windows, el cliente guarda la credencial en Credential Manager sin imprimirla:

```powershell
pwsh ./scripts/Sync-GarminFit.ps1 `
  -Action Pair `
  -PairingToken '<token de emparejamiento creado desde la sesión autenticada>'

pwsh ./scripts/Sync-GarminFit.ps1 `
  -Action Upload `
  -FitPath 'C:\ruta-privada\garmin-activity-123.fit' `
  -GarminActivityId 123
```

El FIT permanece fuera del repositorio. `/api/v1/activities` ofrece listado, filtros y detalle con procedencia; `APP-010` completó la relación planificado–realizado y `APP-012` añadió tendencias 4/8/12 con fuentes navegables.

## Evaluación semanal y decisiones

`/evaluations` crea y consulta snapshots semanales inmutables P1–P5, muestra el semáforo con sus razones y conserva datos ausentes como `NULL`/ND. Cada métrica expone evidencia navegable hacia la sesión del plan, la actividad o el check-in que la originó.

Una decisión exige confirmación y narrativa humanas. `adapt` y `reduce` crean una nueva versión del plan en estado `draft` y registran el antes/después exacto; nunca modifican una versión publicada. El contrato está disponible en `/api/v1/evaluations`, OpenAPI y el cliente TypeScript generado.

## Dashboard y mis datos

El inicio prioriza la siguiente sesión, avance de la semana, recuperación, camino de ritmo reciente a ritmo meta, distancia semanal y pendientes. La gráfica separa caminadora/exterior, explica sus ejes y permite abrir los datos exactos y las actividades fuente. Las guardas de consumo gratuito no se muestran al atleta.

Exportación y solicitudes de archivo/eliminación están en **Perfil → Mis datos**. Las descargas son privadas, autenticadas y temporales; una solicitud de ciclo de vida requiere revisión humana y no modifica datos automáticamente.

## Producción y operación

La configuración reproducible vive en `render.yaml`, `src/web/vercel.json` y `.github/workflows/ci.yml`. Producción exige orígenes CORS HTTPS exactos, limita peticiones y tamaños, oculta OpenAPI, publica cabeceras CSP/HSTS y expone liveness/readiness sin datos privados. El frontend tolera el arranque frío del backend gratuito con reintentos acotados y un mensaje visible.

Antes de publicar el contenido preparado en Git, ejecuta:

```powershell
pwsh ./scripts/Test-PublishBoundary.ps1 -GitIndex
pwsh ./scripts/Test-FreeDeployment.ps1
pwsh ./scripts/Test-ProductionHardening.ps1
pwsh ./scripts/Test-BackupCrypto.ps1
```

El [runbook de producción](docs/operations-runbook.md) documenta despliegue, alertas, suspensión, respaldo cifrado, restauración aislada, rotación de secretos, rollback y piloto de siete días. La evidencia de APP-013 se conserva sin secretos en `docs/app-013-production-acceptance.md`.

## Licencia y contribuciones

El código se publica bajo [licencia MIT](LICENSE). Antes de contribuir, lee [CONTRIBUTING.md](CONTRIBUTING.md), especialmente el límite que prohíbe datos de salud o entrenamiento reales, rutas, credenciales y respaldos.
