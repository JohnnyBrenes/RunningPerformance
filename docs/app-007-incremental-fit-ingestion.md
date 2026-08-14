# APP-007 — Ingestión incremental FIT

**Versión:** `APP-007-v1-2026-08-13`  
**Estado:** completada localmente con datos exclusivamente sintéticos  
**Incremento:** I5

## Resultado

El FIT incremental ya recorre un único pipeline auditable desde carga manual o sincronizador local hasta PostgreSQL. El archivo original se recibe por streaming acotado, se identifica por SHA-256, se guarda en Storage privado y se procesa fuera del request mediante el Worker con lease. La publicación de resumen, sesiones, vueltas, eventos, zonas y muestras ocurre en una sola transacción.

`Tools/FitProcessor` conserva su CLI y delega ahora en `RunningPerformance.Fit`. Esa librería mantiene el contrato canónico determinista de GAR-008 y agrega la normalización relacional que consume el Worker. No se incorporaron FIT reales ni datos del atleta a `App/`.

## Flujo e identidad

1. La API exige nombre, MIME FIT y un ID Garmin positivo proveniente del contexto de descarga.
2. El cuerpo se escribe una sola vez en un temporal con límite de 50 MB y SHA-256 incremental; la firma `.FIT` se revisa antes de Storage.
3. El objeto privado se reutiliza por propietario/hash. Cada origen nuevo conserva su propio `source_file`, ejecución, ítem y auditoría.
4. El Worker comprueba tamaño y hash del objeto descargado, firma, CRC/integridad y lectura completa mediante `Garmin.FIT.Sdk` 21.205.0.
5. El ID Garmin resuelve primero la identidad externa. Sin ID previo, un enriquecimiento histórico sólo se aplica ante una coincidencia única por segundo local exacto, categoría compatible, duración a ±2 s y distancia dentro de `max(20 m, 0.2%)`.
6. Mismo ID y mismo hash es un duplicado seguro. Mismo ID con otro hash, o mismo hash con otro ID, conserva ambos recibos y abre cuarentena.

El reproceso usa el objeto privado ya almacenado. Desactiva el intento vigente y sustituye todo el detalle derivado dentro de la misma transacción; un fallo revierte el cambio completo.

## Precedencia y persistencia

Los valores grabados y presentes en FIT sustituyen resúmenes equivalentes del CSV. Una ausencia FIT conserva el valor histórico, y el título derivado de Garmin Connect tampoco se reemplaza por un nombre técnico del dispositivo. Cada campo seleccionado apunta a su observación de procedencia.

Las muestras se insertan en lotes configurables de 500 filas. Las zonas distinguen su referencia de mensaje, evitando colisiones cuando un FIT incluye más de un bloque del mismo tipo. La proyección preventiva de base usa 25 veces el tamaño del FIT y bloquea antes del umbral gratuito de 400 MB; Storage conserva los umbrales 700/850 MB.

## Sincronizador y seguridad

- La sesión autenticada crea un pairing token aleatorio de uso único y diez minutos.
- El intercambio anónimo está encapsulado en una función `SECURITY DEFINER` ejecutable sólo por `rp_api`.
- La credencial contiene un secreto aleatorio de 256 bits; la base conserva únicamente HMAC-SHA-256 con un pepper obligatorio en producción.
- Su alcance único es `fit.upload`, vence en 90 días, puede revocarse y actualiza `last_used_at`.
- `scripts/Sync-GarminFit.ps1` guarda el valor en Windows Credential Manager, genera una clave idempotente desde ID/hash y nunca imprime el secreto.
- Reutilizar una clave idempotente con otro ID o contenido devuelve conflicto; repetir exactamente la misma carga reutiliza el recibo original.

## Superficie observable

La API ofrece carga manual, reproceso, pairing, listado/revocación de clientes y carga restringida. El cliente OpenAPI TypeScript fue regenerado. El script de Windows cubre pairing y upload; la consulta autenticada de ejecuciones y `/api/v1/activities` permiten revisar estado, resumen y procedencia.

Las actividades sí forman parte del producto visible, no sólo de la recolección. El backend ya lista, filtra y entrega detalle. APP-010 añadirá su relación con sesiones planificadas y APP-012 completará el dashboard 4/8/12 semanas y sus gráficas.

## Evidencia sintética

- Build .NET Release: 8 proyectos, 0 errores y 0 advertencias.
- Restore .NET con lockfiles: aprobado.
- pgTAP: 81 pruebas aprobadas; lint SQL sin errores.
- Unitarias .NET: 13 aprobadas, incluidas generación, determinismo, normalización y rechazo CRC de un FIT sintético.
- Integración .NET: 2 aprobadas con PostgreSQL en Testcontainers.
- Vitest: 7 aprobadas; TypeScript y build Vite de producción aprobados.
- Imagen combinada API + Worker: `running-performance-api:app007-local`, build aprobado.
- Cliente Credential Manager: C# interop compilado; un archivo ausente se rechazó antes de acceder a credenciales.
- Ensayo local API/Storage/Worker/PostgreSQL:
  - primera importación: `succeeded`;
  - reintento con la misma clave: `reusedReceipt=true`;
  - reproceso sin nueva descarga: `succeeded`;
  - mismo ID con otro hash: `quarantined`;
  - resultado: 1 actividad, 11 muestras, 2 archivos de origen, 3 ejecuciones, 3 intentos y 1 intento vigente.

El ensayo también descubrió y corrigió dos defectos antes del cierre: lectores Npgsql abiertos al confirmar algunas transacciones y el MIME `application/vnd.ant.fit` ausente en el bucket privado.

## Operación local

DBeaver puede conectarse a `127.0.0.1:54322`, base/usuario/contraseña `postgres`, con SSL desactivado. El esquema principal es `app`. La conexión administrativa omite el comportamiento efectivo de RLS; las pruebas de aislamiento deben continuar usando los roles `rp_api`/`rp_worker` y `SET LOCAL`.

El chequeo de límite publicable encontró un archivo local preexistente `src/web/.env.local`. Está cubierto por `.gitignore`, no se leyó ni modificó, pero debe retirarse o trasladarse antes de ejecutar una publicación material. No se encontraron FIT, TCX, GPX, ZIP o HAR bajo `App/`.

## Alcance no ejecutado

No se modificó la base productiva, no se cargaron FIT reales, no se usaron componentes pagados y no se avanzó a APP-010. La activación de la automatización Garmin real continúa siendo una acción privada y explícita del propietario.
