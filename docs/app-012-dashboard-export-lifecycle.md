# APP-012 — Seguimiento práctico y gestión de datos

**Versión:** `APP-012-v2-2026-08-13`  
**Incremento:** I8  
**Estado:** completado localmente

## Resultado

I8 entrega un inicio centrado en decisiones cotidianas: la siguiente sesión, avance semanal, recuperación, próxima carrera, distancia por semana, revisión semanal y pendientes. Las opciones administrativas no compiten con el entrenamiento: exportación, archivo y eliminación viven únicamente bajo **Perfil → Mis datos**.

La base de pruebas del usuario `athlete-a@example.invalid` conserva el historial ya cargado: 460 registros del CSV más 3 actividades sintéticas de soporte. Las tendencias usan las 298 actividades clasificadas como running, mantienen caminadora y exterior separadas y permiten navegar a cada actividad fuente.

## Experiencia práctica

- El inicio abre en cuatro semanas y permite comparar 4, 8 o 12 semanas.
- “Camino a tu meta” compara el ritmo reciente ponderado de cuatro semanas con el ritmo objetivo de la próxima carrera, explica que no todo entrenamiento debe correrse a ritmo meta y muestra el siguiente paso del plan.
- La gráfica indica explícitamente que X es la semana de inicio y Y los kilómetros acumulados; usa fechas legibles y una serie amarilla contrastante para exterior.
- La revisión P1–P5 se resume en una acción: continuar, continuar con cautela o detener y revisar. El detalle y la evidencia permanecen disponibles al abrir la revisión.
- Los datos exactos y sus fuentes están en un bloque desplegable para no saturar la vista principal.
- Las alertas de cuota se conservan como guardas internas, pero no aparecen como “Consumo gratuito” en el dashboard del atleta.
- `/activities` permite consultar el historial y abrir la procedencia de una actividad.
- En Carreras, la zona horaria se elige de un catálogo IANA válido; la ciudad o sede sigue siendo opcional, se limita y se limpia sin introducir geocodificación externa.
- Al editar una carrera o revisar su meta se oculta temporalmente la lista completa para mostrar sólo el registro seleccionado; guardar o cancelar devuelve al listado.

## Exportación y ciclo de vida

- La exportación JSON se genera para el propietario autenticado, se guarda en un objeto privado y vence en 24 horas.
- La descarga pasa por la API autenticada; no se crea una URL pública.
- La creación es idempotente y conserva versión de esquema, estado y auditoría.
- Una solicitud de archivo o eliminación queda pendiente de revisión humana. No borra ni archiva datos automáticamente.
- RLS y pruebas negativas impiden leer o descargar recursos de otro propietario.

## Superficie entregada

- Migración `0160_dashboard_export_lifecycle.sql`.
- API autenticada para dashboard, exportaciones, descarga y solicitudes de ciclo de vida.
- OpenAPI y cliente TypeScript regenerados.
- Dashboard responsive, historial `/activities` y panel secundario **Perfil → Mis datos**.
- Agregados 4/8/12 semanas con `NULL`/ND preservado y evidencia navegable.

## Validación

| Puerta | Resultado |
|---|---:|
| Migraciones locales | 17 |
| Lint de base de datos | 0 errores |
| pgTAP | 129 aprobadas |
| Contrato de esquema | 45 tablas, 9 vistas |
| .NET unitarias | 39 aprobadas |
| .NET integración | 2 aprobadas |
| Build .NET Release | 0 errores, 0 advertencias |
| Vitest | 17 aprobadas |
| TypeScript/lint | aprobado |
| Build Vite | aprobado |
| Playwright | 11 aprobadas, 4 omitidas por perfil |

Playwright cubrió Chromium a 320 px, WebKit a 390 px y Chromium de escritorio. Las dos mutaciones auditables se ejecutan una sola vez en escritorio y se omiten deliberadamente en los perfiles compactos. Los fallos de selectores detectados durante la pasada final se corrigieron y sólo se repitieron los casos afectados.

## Límites preservados

- No se modificó producción.
- No se modificaron actividades Garmin.
- No se habilitaron componentes pagados ni facturación.
- El historial CSV se usó únicamente en la base local de pruebas autorizada.
- `APP-013` permanece pendiente y no fue activada.
- `MON-001` no cambió porque no llegaron datos deportivos nuevos.
