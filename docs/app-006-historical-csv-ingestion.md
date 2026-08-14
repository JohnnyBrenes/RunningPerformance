# APP-006 — Importación histórica CSV

**Estado:** completada, incluida la importación real alojada  
**Versión:** `APP-006-v1-2026-08-12`  
**Incremento:** I4

## Resultado

El flujo histórico CSV quedó operativo de punta a punta contra Supabase local en Docker y contra el proyecto productivo `Running Performance` de Supabase Free en `us-east-1`. La API recibe el archivo normalizado por stream, aplica límite antes y durante la lectura, calcula SHA-256 mientras escribe un temporal y guarda el original en el bucket privado `athlete-files` sin upsert. La recepción crea `stored_object`, `source_file`, `ingestion_run`, un ítem envelope y auditoría dentro de una transacción RLS.

El Worker hospedado en la API reclama trabajos PostgreSQL con `FOR UPDATE SKIP LOCKED`, lease, heartbeat, reintentos con backoff y recuperación de lease vencido. El reclamo global cruza propietarios únicamente mediante `app.claim_csv_ingestion_run`, una función `SECURITY DEFINER` estrecha ejecutable por `rp_worker`; heartbeat, reintentos y cuarentena vuelven a transacciones RLS del propietario. Descarga exclusivamente la ruta persistida, valida las 57 columnas y las 460 filas completas y sólo publica cuando el lote entero es válido. Una publicación inserta o reconcilia actividades, ítems, observaciones y fuentes de campo dentro de una sola transacción.

## Contrato e idempotencia

- Contrato versionado `activities-normalized-v1`, con encabezado exacto y UTF-8.
- Validación explícita de fila fuente, SHA-256 provisional, ID Garmin, fecha local sin offset inventado, tipos numéricos, unidades, `boolean`, conteo y colisiones dentro del archivo.
- Distancias pasan a metros, velocidades a m/s y ritmos de natación `per_100m` a segundos/km; el original permanece en `summary_payload`.
- Valores vacíos y `--` se conservan como `NULL`; el sentinel Garmin `distance_value=0` sin unidad también se interpreta como distancia ausente. La respuesta API mantiene esos `NULL` y nunca los presenta como cero.
- La clave `(owner_id, provisional_activity_key)` impide duplicados y la clave Garmin opcional detecta colisiones.
- Repetir un mismo archivo reutiliza el objeto por `(owner_id, sha256)`, pero conserva un nuevo `source_file`, una nueva ejecución y nuevas observaciones por fila.
- Un error de contrato persiste errores por fila y deja cero publicaciones parciales. Conflictos de identidad van a cuarentena; fallos transitorios se reintentan hasta el límite.

## Superficie entregada

- `POST /api/v1/ingestion-runs/historical-csv?fileName=...` con cuerpo `text/csv` y respuesta `202`.
- `GET /api/v1/ingestion-runs/{id}` con estado, progreso, conteos, hash y errores sanitizados.
- `GET /api/v1/activities` con filtros, orden y paginación desde servidor.
- `GET /api/v1/activities/{id}` con métricas, hora local/UTC y procedencia navegable.
- `scripts/New-SyntheticHistoricalCsv.ps1` produce el fixture reproducible de 460 filas fuera del repositorio.
- `scripts/Import-HistoricalActivities.ps1` carga y espera una ejecución autenticada sin copiar el CSV al repositorio.
- OpenAPI 3.1 y cliente TypeScript regenerados.

## Evidencia

- Migraciones `0110` y `0120` aplicadas de forma no destructiva; 13 migraciones productivas en total y lint del esquema `app` sin errores.
- 67/67 pruebas pgTAP, incluidos FK compuesta del archivo, índice claimable, triggers de progreso y privilegios exclusivos de `rp_worker` sobre la función de reclamo.
- Build .NET Release sin warnings.
- 11/11 pruebas unitarias y 2/2 pruebas de integración, con fixture sintético de 460 filas, modalidades variadas, comillas CSV, nulos, colisión segura y contexto RLS transaccional.
- Doble importación sintética end-to-end: ambas ejecuciones `succeeded`, 460/460 aplicadas, cero errores y un solo conjunto de 460 actividades.
- Segunda carga reutilizó el objeto privado por hash y cada actividad verificada conservó dos observaciones de procedencia.
- Actividad sintética de fuerza consultada por API conservó `distance_m`, ritmo e ID Garmin ausentes como `NULL`.
- Fixture inválido de 459 filas quedó `failed` con `csv_row_count`, cero éxitos y el conteo de actividades permaneció en 460.
- El CSV real `Data/activities-normalized.csv`, SHA-256 `08803E36E4493A32D20BAA4DE98880E36E6E2F83BFE115987B2747FD28A9A6FA`, se importó dos veces. Las corridas `c7153724-5748-477e-a800-1fa84d9eaaf9` y `ff15fd79-c1f5-484f-b2fa-fb5c026807e2` terminaron `succeeded` con 460/460 y cero fallos.
- La reconciliación alojada final confirmó un usuario Auth, un perfil, dos corridas exitosas, un solo objeto privado de 164,152 bytes, dos archivos fuente aceptados, 460 actividades y 460 claves provisionales distintas.
- Los 143 sentinels de distancia ausente quedaron como `NULL`; existen 920 ítems aplicados, 920 observaciones de fuente, 8,740 selecciones de procedencia de campo y cero claves duplicadas.

## Operación y privacidad

El propietario autorizó explícitamente la creación y la importación. Se creó `Running Performance` en Supabase Free, región `us-east-1`, sin tarjeta, trial, tamaño pagado, add-on ni seed productivo. El registro público quedó deshabilitado y se creó una única cuenta Auth confirmada con perfil `Johnny Brenes`; sus 460 actividades son propiedad de ese UUID. El proyecto preexistente `RunningTracker` no se modificó.

Las claves Supabase, el token del usuario y las credenciales PostgreSQL temporales sólo se usaron en memoria y no se escribieron en el repositorio. La contraseña Auth operativa fue aleatoria y no se conservó; el propietario deberá fijar una propia mediante recuperación cuando la interfaz publicada tenga su URL definitiva. El CSV real permanece fuera de `App/`; Supabase Storage conserva la copia privada recibida por el backend.

`APP-006` queda completada. `APP-007` es la siguiente tarea técnica elegible y permanece pendiente hasta activación explícita.
