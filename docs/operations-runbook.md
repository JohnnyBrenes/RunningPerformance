# Runbook de producción

Este runbook opera el MVP en GitHub Free, Vercel Hobby, Render Free y Supabase Free. No autoriza activar trials, añadir un medio de pago, comprar un dominio ni habilitar recursos que facturen. Los valores secretos se configuran únicamente en los paneles de cada proveedor.

## Arquitectura operativa

- GitHub contiene únicamente este repositorio público. GitHub Actions está deshabilitado y todas las verificaciones se ejecutan localmente.
- Vercel publica `src/web` como SPA HTTPS.
- Render construye el `Dockerfile` y ejecuta API + Worker en un único Web Service Free.
- Supabase Free ofrece Auth, PostgreSQL y el bucket privado `athlete-files`.
- El equipo local conserva los respaldos cifrados fuera del repositorio y de los servicios publicados.

## Primer despliegue

1. Ejecuta todas las puertas del README y `pwsh ./scripts/Test-PublishBoundary.ps1 -GitIndex` sobre el contenido exacto que se publicará.
2. Crea un proyecto Supabase Free sin medio de pago y aplica, en orden, las migraciones de `supabase/migrations`.
3. Confirma RLS, grants y aislamiento con las pruebas SQL antes de importar datos reales.
4. En Render, crea el Blueprint desde `render.yaml`. Configura `DATABASE_URL`, `SUPABASE_URL`, `SUPABASE_PUBLISHABLE_KEY` y `CORS_ALLOWED_ORIGINS`; esta última contiene exclusivamente el origen HTTPS exacto de Vercel, sin ruta ni comodín.
5. En Vercel Hobby, usa `src/web` como Root Directory y configura `VITE_API_BASE_URL`, `VITE_SUPABASE_URL` y `VITE_SUPABASE_PUBLISHABLE_KEY`.
6. Después de conocer la URL final de Vercel, actualiza `CORS_ALLOWED_ORIGINS` en Render y vuelve a desplegar. No uses el secreto de Supabase en el frontend.
7. Verifica `/health/live`, `/health/ready`, cabeceras de seguridad, login, flujos principales y que OpenAPI no esté disponible en producción.
8. Registra las URLs y la evidencia del proveedor en `docs/app-013-production-acceptance.md`; no copies tokens, cadenas de conexión ni datos del atleta.

## Arranque frío y suspensión

El frontend muestra un aviso después de ocho segundos mientras despierta el backend. Durante el piloto diario:

1. Deja el backend sin tráfico suficiente para permitir su suspensión.
2. Abre la aplicación y mide desde la primera petición hasta que `/health/ready` responde correctamente.
3. Confirma que los reintentos por red, `429` y `5xx` se recuperan sin duplicar escrituras.
4. Registra fecha, dispositivo, duración, resultado y cuota observada, sin payloads ni datos personales.

La falta de heartbeat del Worker por más de diez minutos vuelve no saludable la readiness. Revisa logs estructurados por `correlation_id`, estado HTTP y duración; nunca habilites logging de cuerpo, query string o autorización.

## Alertas e incidentes

- `401/403` inesperado: valida emisor/clave pública de Supabase y los claims; no relajes RLS.
- `429`: respeta `Retry-After` y revisa abuso o bucles antes de modificar límites.
- `/health/live` sano y `/health/ready` no sano: revisa base de datos, heartbeat Worker y trabajos pendientes o con lease vencido.
- Ingestión estancada o cuarentena abierta: conserva originales, inspecciona por identificadores internos y reintenta sólo cuando la causa sea conocida.
- Cuota en advertencia: detén importaciones y exportaciones grandes. Al llegar al límite de bloqueo, conserva la degradación controlada; no habilites cobro automático.
- Posible secreto expuesto: revócalo y rótalo primero, revisa logs/auditoría, actualiza el proveedor afectado y sólo después limpia referencias. Considera comprometido el valor histórico.

## Respaldo

Prerrequisitos: sesión autenticada de Supabase CLI vinculada al proyecto correcto, dependencias instaladas y un directorio privado fuera de `App` y de cualquier carpeta sincronizada públicamente.

```powershell
pwsh ./scripts/Test-BackupCrypto.ps1
pwsh ./scripts/New-ProductionBackup.ps1 -BackupDirectory 'D:\PrivateBackups\RunningPerformance'
```

El comando exporta roles, esquema, datos y los objetos privados de `athlete-files`; crea manifiestos SHA-256 y un único `.rpbak` cifrado con una frase de al menos 14 caracteres. Guarda el archivo, su `.sha256` y la frase en ubicaciones separadas. Nunca agregues esos archivos a Git.

Frecuencia mínima durante el piloto: un respaldo antes de desplegar o migrar y otro al terminar cada día con cambios reales. Conserva al menos el último respaldo anterior y posterior a cada despliegue.

## Prueba de restauración

La restauración sólo se prueba contra un proyecto vacío y desechable. El script rechaza destinos remotos salvo confirmación explícita.

```powershell
$env:RESTORE_DATABASE_URL='postgresql://synthetic-user:synthetic-password@127.0.0.1:54322/postgres'
$env:RESTORE_SUPABASE_URL='http://127.0.0.1:54321'
$env:RESTORE_SUPABASE_SECRET_KEY='synthetic-local-secret-key'
pwsh ./scripts/Test-ProductionRestore.ps1 `
  -BackupPath 'D:\PrivateBackups\RunningPerformance\running-performance-YYYYMMDDTHHMMSSZ.rpbak' `
  -ConfirmIsolatedTarget
```

Después valida conteos, RLS con dos identidades sintéticas, descargas privadas y SHA-256 de Storage. Destruye el destino desechable desde su propio entorno; nunca ejecutes `db reset` sobre el proyecto vinculado de producción.

## Despliegue y rollback

1. Crea un respaldo cifrado y registra el commit desplegado.
2. Publica únicamente desde una revisión que haya pasado todas las puertas locales documentadas.
3. Verifica readiness y los flujos de humo antes de importar o modificar datos.
4. Si falla sólo la SPA, promueve el último deployment sano de Vercel.
5. Si falla API + Worker, restaura el deployment sano anterior de Render o despliega el commit anterior conocido.
6. Si una migración incompatible alcanzó producción, detén escrituras, restaura en un proyecto Supabase nuevo y aislado, valida, cambia los secretos del backend y conserva el proyecto anterior intacto hasta completar la reconciliación.

No reviertas una migración destructivamente ni sobrescribas originales para acelerar un rollback.

## Piloto de siete días

Cada día registra: acceso desde iPhone y PC, arranque frío, login, calendario/sesión, ejercicios, historial, captura subjetiva, dashboard, carga manual, Worker/readiness, ingestiones pendientes, cuarentena, cuotas de Vercel/Render/Supabase y costo obligatorio observado. El día inicial y final deben incluir respaldo; al menos una restauración aislada debe quedar probada.

La aceptación exige siete días consecutivos, costo obligatorio USD 0, smoke real en iPhone instalado como acceso de pantalla de inicio y una fuente de verdad operativa documentada. Una prueba automatizada WebKit ayuda, pero no sustituye el dispositivo físico.
