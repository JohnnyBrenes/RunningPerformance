# APP-005 — Cimientos, esquema y aislamiento

**Versión:** `APP-005-v1-2026-08-12`  
**Estado:** completada el 2026-08-12; integración activa exclusivamente en Docker local/CI.

## Resultado

- Solución .NET 10 modular, SPA React/Vite y dependencias npm/NuGet bloqueadas.
- OpenAPI 3.1 generado durante el build y cliente TypeScript `fetch` regenerado sin edición manual.
- Imagen multietapa API + Worker con bases .NET fijadas por digest, usuario no-root y perfil verificado de 512 MB/0.1 CPU.
- 45 tablas, ocho vistas, 69 FKs compuestas de propietario, siete índices parciales, RLS habilitada/forzada y roles sin `BYPASSRLS`.
- Bucket privado `athlete-files`, Auth local y dos propietarios exclusivamente sintéticos.
- Guardas preventivas: base alerta a 300 MB/bloquea a 400 MB; Storage alerta a 700 MB/bloquea a 850 MB; facturación siempre deshabilitada.
- CI para restore bloqueado, formato, build, pruebas, auditoría de dependencias, contrato generado, límite publicable, esquema/RLS/Storage y build Docker.

## Pruebas ejecutadas

- `dotnet restore --locked-mode`, build: 0 errores y 0 advertencias.
- 8 pruebas unitarias y 1 prueba de integración .NET aprobadas.
- 4 pruebas Vitest, type-check y build Vite aprobados.
- `npm audit`: 0 vulnerabilidades.
- `supabase db reset` reconstruyó migraciones y seed desde cero.
- `supabase db lint --schema app --level error`: 0 errores.
- 29 pruebas pgTAP aprobadas localmente; el setup usa `postgres` sólo dentro de la transacción de prueba para ser compatible con el rol efímero de la CLI alojada.
- Auth + Storage: carga/lectura propia permitida; lectura y carga cruzadas denegadas.
- Contenedor `running-performance-app005`: `/health/live` y `/health/ready` devuelven `200 Healthy`; Worker hospedado activo.

## Contenedores locales

La pila local visible en Docker Desktop contiene cinco servicios funcionales de Supabase (`db`, `auth`, `storage`, `rest`, `kong`) y dos herramientas de desarrollo (`studio`, `pg_meta`). Edge Runtime, Realtime, Analytics, correo, Image Proxy, Vector y Pooler están desactivados. Estos contenedores reproducen Supabase local; producción consumirá Supabase administrado y no los desplegará.

El octavo contenedor es `running-performance-app005`, nuestro backend combinado API + Worker. La SPA se construye como estático para Vercel Hobby y no requiere un contenedor de producción.

## Integración local

- Supabase local en Docker es el único entorno de integración persistente; CI reconstruye la misma pila de forma efímera.
- Nueve migraciones y `supabase/seed.sql` idempotente crean sólo dos propietarios sintéticos.
- Las suites pgTAP revierten sus cambios; la prueba de limpieza de contexto elimina y confirma su única fila temporal.
- El smoke Auth/Storage local usa objetos sintéticos y verifica acceso propio/denegación cruzada.
- El proyecto preexistente `RunningTracker` no está enlazado y queda fuera de alcance.

## Validación alojada temporal

Se creó temporalmente `running-performance-integration` en Supabase Free `us-east-1`, se verificaron PostgreSQL 17, nueve migraciones, seed sintético, configuración Auth, lint sin errores, 29 pruebas pgTAP y aislamiento Auth/Storage. Por decisión explícita del propietario, el proyecto se eliminó el 2026-08-12 después de las pruebas y el enlace local fue retirado. No queda integración alojada ni producción; el segundo cupo Free está disponible para una futura base productiva.

## Límite gratuito y de datos

Toda la ruta de publicación conserva costo obligatorio USD 0: Docker local/CI durante desarrollo y, más adelante, Supabase Free, Render Free, Vercel Hobby y GitHub Free para producción. No se registró tarjeta, no se solicitó tamaño de cómputo, alta disponibilidad, trial, add-on, dominio comprado ni auto-upgrade. Local, CI e integración contienen únicamente datos sintéticos; los datos deportivos reales permanecen fuera hasta APP-006 y sus puertas correspondientes.

## Ajustes de seguridad durante I0

- `react-router` se elevó de `7.18.1` a `7.18.2` por el advisory CSRF alto.
- `Microsoft.OpenApi` se fijó en `2.7.5`, primera corrección 2.x del advisory de referencias circulares.
- `openapi-typescript 7.13.0` se rechazó porque su peer solo admite TypeScript 5.x; se adoptó `openapi-typescript-codegen 0.31.0` sin forzar la resolución ni degradar TypeScript 7.
