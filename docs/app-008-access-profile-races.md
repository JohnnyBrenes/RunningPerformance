# APP-008 — Acceso, shell, perfil y carreras

**Estado:** completada el 2026-08-12  
**Versión:** `APP-008-v1-2026-08-12`  
**Incremento:** I2

## Resultado

El primer corte vertical está operativo contra Supabase local en Docker. La SPA usa Supabase únicamente para la sesión; todas las operaciones de dominio atraviesan `/api/v1`, donde ASP.NET Core valida el JWT y Npgsql aplica el propietario verificado dentro de una transacción RLS.

La interfaz ofrece acceso por correo/contraseña, recuperación, cierre de sesión con limpieza de caché, shell mobile-first, perfil, antecedentes de salud, carreras y metas con historial inmutable. En una meta de carrera el usuario captura el tiempo y el ritmo se calcula automáticamente a partir de la distancia para evitar combinaciones inconsistentes.

## Seguridad y aislamiento

- Registro público deshabilitado y dos cuentas exclusivamente sintéticas en local/CI.
- Validación de firma por JWKS, issuer, audience `authenticated`, expiración, `sub` UUID y rol `authenticated`.
- Política de autorización fallback; ningún endpoint de dominio acepta `userId`.
- Cada unidad de trabajo ejecuta `SET LOCAL ROLE rp_api` y fija `request.jwt.claim.sub`, `role` y `claims` dentro de la transacción.
- La prueba de pool fuerza una sola conexión física y confirma que rol y propietario desaparecen tras el commit.
- La función estrecha `app.create_race_goal_version` deriva el propietario del contexto, bloquea la carrera, conserva versiones anteriores, deja una sola meta actual y audita el cambio.
- Una prueba con token del propietario B y el ID conocido de una carrera A devolvió `404`.

## Superficie entregada

- `GET/PUT /api/v1/profile`.
- `GET/POST/PUT /api/v1/health-contexts`.
- `GET/POST/PUT /api/v1/races`.
- `GET/POST /api/v1/races/{id}/goals`.
- Cliente TypeScript regenerado desde OpenAPI 3.1.
- Estados de carga, error y vacío; navegación inferior móvil y barra lateral de escritorio; safe areas, blancos táctiles de 44 px y ausencia de scroll horizontal en los flujos principales.
- Carga diferida por ruta para mantener pequeños los fragmentos iniciales.

## Evidencia

- Restauración completa de 10 migraciones y seed sintético.
- Lint SQL sin errores y 37/37 pruebas pgTAP.
- Build .NET sin warnings; 8/8 pruebas unitarias y 2/2 de integración.
- 4/4 pruebas Vitest y 9/9 smokes Playwright: Chromium 320 px, WebKit 390 px y Chromium escritorio.
- Los smokes escriben y vuelven a leer contexto, carrera, meta calculada e historial versionado.
- Build Vite de producción por fragmentos y auditoría npm con cero vulnerabilidades.
- Imagen Docker reproducible, no-root (`1654`), con `/health/live` y `/health/ready` en 200 contra PostgreSQL local.
- Contratos de 45 tablas/ocho vistas, límite publicable, costo gratuito y aislamiento Auth/Storage aprobados.
- Recuperación de contraseña local aceptada por Supabase Auth con respuesta 200 para la cuenta sintética.

## Operación local

Tras un reinicio de Windows, Kong no pudo montar su certificado porque el origen temporal de Docker había reaparecido como directorio. Un `stop` con backup seguido de `start` regeneró los secretos de montaje y dejó Kong saludable sin borrar la base. El comando reproducible es:

```powershell
npm --prefix src/web run db:repair
```

El frontend local usa `http://127.0.0.1:5173` y la API `http://127.0.0.1:5080`; Docker Desktop ocupa el puerto 8080 en esta máquina. Las instrucciones completas están en `README.md`.

## Cambios de toolchain

El SDK fijado se movió de `10.0.302` a `10.0.400` porque el primero dejó de estar instalado después del reinicio del equipo. La imagen SDK está fijada por digest. `SSH.NET` se fijó directamente en `2026.0.0` para evitar la versión transitiva afectada por [GHSA-q939-rpr3-3284](https://github.com/advisories/GHSA-q939-rpr3-3284) incluida por Testcontainers; los lockfiles quedaron regenerados.

## Fuera de alcance

No se creó producción, proyecto Supabase alojado, servicio pagado ni importación de datos reales. `APP-009` queda como siguiente tarea técnica, aún sin activar.
