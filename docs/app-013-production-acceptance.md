# APP-013 — Endurecimiento, despliegue y aceptación del MVP

Estado: **en progreso**  
Inicio: **2026-08-14**  
Repositorio objetivo: **https://github.com/JohnnyBrenes/RunningPerformance**

APP-013 no se considera terminada con la publicación del código. La aceptación completa exige despliegue real gratuito, respaldo y restauración aislada, prueba física en iPhone y un piloto de siete días.

## Entregado localmente

- API en producción con CORS fail-closed, HSTS/CSP y cabeceras defensivas, límites de cuerpo/cabeceras, rate limits separados y OpenAPI sólo en Development.
- Telemetría sin payloads: método, ruta, estado, duración y correlación; métricas de peticiones, errores, jobs y heartbeat del Worker.
- Readiness dependiente de PostgreSQL y heartbeat; alerta de jobs pendientes, leases vencidos y cuarentena abierta.
- Frontend con CSP/HSTS en Vercel, reintentos acotados para red/`429`/`5xx` y mensaje visible de arranque frío.
- `DATABASE_URL` compatible con formato clave-valor y URI PostgreSQL de producción, incluyendo credenciales URL-encoded.
- Backup lógico de PostgreSQL + Storage privado, manifiestos SHA-256, cifrado autenticado fuera de Git y restauración que rechaza destinos no aislados por defecto.
- Blueprint Render Free, configuración Vercel, verificación exclusivamente local, SBOM npm, auditorías, contenedor no-root, licencia MIT y política de contribución privada-por-defecto.
- Runbook de despliegue, suspensión, incidentes, cuotas, secretos, backup, restauración y rollback.

## Evidencia ejecutada el 2026-08-14

| Puerta | Resultado |
|---|---|
| `dotnet format --verify-no-changes` | pasa |
| build Release .NET | pasa, 0 warnings / 0 errors |
| pruebas .NET | 43 pasan: 39 unitarias + 4 integración |
| type-check web | pasa |
| Vitest | 28 pasan en 8 archivos |
| build Vite | pasa; 49 archivos, aproximadamente 42.3 MB con assets |
| Playwright | 17 pasan y 4 skips esperados en perfiles Chromium 320, WebKit 390 y desktop |
| pgTAP | 137 pasan |
| lint SQL | 0 errores |
| auditoría npm | 0 vulnerabilidades |
| auditoría NuGet transitiva | ningún paquete vulnerable reportado |
| Docker | construye; usuario final no-root `1654`, puerto `8080`, aproximadamente 100.2 MB |
| hardening/costo/cripto | pasan `Test-ProductionHardening`, `Test-FreeDeployment` y `Test-BackupCrypto` |
| límite publicable | pasa sobre el índice Git; 318 archivos iniciales y aproximadamente 42.8 MB antes de esta documentación |
| rate limit de humo | 30 respuestas autenticables/`401`; petición anónima 31 devuelve `429`; health continúa `200` |

La prueba de cifrado demuestra roundtrip y rechazo de manipulación. No equivale aún a una restauración completa de producción; esa evidencia permanece abierta.

## Criterios MVP

| # | Estado | Evidencia o pendiente |
|---:|---|---|
| 1 | verificado local | Auth/RLS y pruebas negativas con dos propietarios sintéticos. |
| 2 | verificado local | Reimportación histórica idempotente. |
| 3 | verificado local | 460 filas sintéticas reconciliadas; ausentes conservados como nulos. |
| 4 | verificado local | FIT válido e idempotencia cubiertos. |
| 5 | verificado local | conflicto ID/hash conserva orígenes y entra en cuarentena. |
| 6 | verificado local | historial, filtros, detalle y procedencia cubiertos. |
| 7 | verificado local | vínculo planificado–realizado y P4/P5 cubiertos. |
| 8 | verificado local | ejercicios ordenados, prescripción, texto, assets y accesibilidad cubiertos. |
| 9 | parcial | Chromium/WebKit y breakpoints pasan localmente; falta smoke contra producción en iPhone y Chrome/Edge de PC. |
| 10 | verificado local | snapshot P1–P5 y decisión humana confirmada cubiertos. |
| 11 | verificado local | ajuste crea una versión nueva y auditable. |
| 12 | verificado local | dashboard navega hasta fuentes y actividades. |
| 13 | verificado local | contenido Git pasa escaneo, builds y pruebas locales; el repositorio público no usa GitHub Actions. |
| 14 | parcial | procedimiento y criptografía probados; falta backup real y restauración completa en destino aislado. |
| 15 | pendiente | falta publicar y comprobar GitHub/Vercel/Render/Supabase, sin tarjeta/trial y con costo obligatorio USD 0. |
| 16 | pendiente | requiere iPhone físico: añadir a inicio, icono/nombre, standalone, login y flujos conectados. |

## Despliegue gratuito que debe comprobarse

- [GitHub](https://docs.github.com/en/get-started/learning-about-github/githubs-plans): repositorio público usado sólo para alojar el código, sin GitHub Actions ni secretos personales en el contenido.
- [Vercel Hobby](https://vercel.com/docs/plans/hobby): uso personal/no comercial, sin dominio comprado ni add-ons; el bundle está por debajo del límite de carga documentado.
- [Render Free](https://render.com/docs/free): un Web Service, filesystem efímero, suspensión/arranque frío esperados y 750 horas mensuales compartidas en el workspace.
- [Supabase Free](https://supabase.com/pricing): Auth/PostgreSQL/Storage dentro de cuotas; los backups de base no contienen los objetos de Storage, que se copian aparte según la [documentación oficial](https://supabase.com/docs/guides/platform/backups).

Antes de aceptar el criterio 15 se deben guardar capturas o exportes sanitizados que muestren el plan Free/Hobby y ausencia de método de pago/trial. Ninguna captura con claves o datos personales entra al repositorio.

## Piloto de siete días

El piloto comienza únicamente después de que frontend, backend y Supabase estén accesibles por HTTPS y se haya creado el respaldo inicial.

| Día | Fecha | iPhone/PC | arranque frío | flujos | Worker/ingestión | cuotas/costo | evidencia |
|---:|---|---|---|---|---|---|---|
| 1 | pendiente |  |  |  |  |  |  |
| 2 | pendiente |  |  |  |  |  |  |
| 3 | pendiente |  |  |  |  |  |  |
| 4 | pendiente |  |  |  |  |  |  |
| 5 | pendiente |  |  |  |  |  |  |
| 6 | pendiente |  |  |  |  |  |  |
| 7 | pendiente |  |  |  |  |  |  |

## Cierre requerido

1. Repetir todas las puertas locales sobre el commit exacto que se vaya a desplegar.
2. Desplegar Supabase, Render y Vercel en planes genuinamente gratuitos y registrar URLs sanitizadas.
3. Crear backup cifrado real y restaurarlo en un destino vacío/aislado; validar DB, RLS, Storage y hashes.
4. Ejecutar smoke de producción en Chrome/Edge y Safari de un iPhone real.
5. Instalar desde “Añadir a pantalla de inicio”, abrir standalone y completar los flujos principales con conexión.
6. Completar siete días de piloto, comprobar cuotas/costo USD 0 y documentar la fuente de verdad.

Procedimientos detallados: [operations-runbook.md](operations-runbook.md).
