# Requerimientos funcionales — Running Performance App

**Versión:** `APP-001-v6-2026-08-14`
**Estado:** aprobado  
**Usuario inicial:** un atleta  
**Persistencia objetivo:** Supabase PostgreSQL  
**Tecnologías condicionantes:** React y C#/.NET; la arquitectura exacta corresponde a `APP-003`

## 1. Propósito

La aplicación será la fuente de verdad histórica para planificar entrenamiento, conservar actividades, comparar lo planificado con lo realizado, ejecutar evaluaciones semanales y registrar ajustes sustentados en evidencia.

El producto no pretende reemplazar Garmin Connect ni diagnosticar condiciones médicas. Su valor es integrar el plan, el historial resumido, el detalle FIT disponible, la captura subjetiva y las decisiones de entrenamiento en un único sistema auditable.

## 2. Actores

### Atleta

Único usuario funcional inicial. Consulta y administra su perfil, carreras, plan, actividades, métricas, evaluaciones y decisiones.

### Sincronizador local Garmin

Cliente técnico ejecutado en la PC del atleta. Descubre IDs y obtiene FIT usando la sesión autenticada local ya validada. No es un usuario interactivo, no almacena credenciales y debe autenticarse de forma restringida ante el backend cuando envíe archivos.

### Operador

El propio atleta en funciones de mantenimiento: ejecuta importaciones, revisa colisiones, reintenta fallos y consulta auditoría. No se crea un sistema administrativo multiusuario para el MVP.

## 3. Alcance del MVP

### Incluido

1. Inicio de sesión de un único atleta.
2. Perfil deportivo y configuración básica.
3. Carreras objetivo y objetivos versionados.
4. Plan y sesiones planificadas versionadas.
5. Guías de ejercicios para fuerza, movilidad y pliometría con prescripción, descripción breve y hasta dos imágenes.
6. Historial importado desde el staging normalizado de `Activities.csv`.
7. Ingestión incremental y carga manual de FIT.
8. Listado y detalle de actividades con procedencia.
9. Enlace entre sesión planificada y actividad realizada.
10. Captura manual de RPE, dolor, fatiga, sueño, recuperación y síntomas.
11. Evaluaciones semanales P1–P5 y semáforo de seguridad.
12. Ajustes del plan con evidencia y auditoría.
13. Dashboard de entrenamiento y tendencias comparables.
14. Exportación de datos consolidados del atleta.
15. Operación segura para un repositorio GitHub sin datos personales ni secretos.
16. Publicación completa mediante planes gratuitos, sin cuota mensual, add-ons pagados ni dominio comprado.
17. Instalación desde Safari como acceso directo en la pantalla de inicio del iPhone, con icono y apertura independiente.

### Después del MVP

- Análisis asistido por IA con evidencia trazable y aprobación humana.
- Catálogo de calzado/equipamiento y kilometraje por artículo.
- Mapas y visualización avanzada de rutas.
- Ingestión longitudinal automática de sueño, HRV, estrés o Training Status.
- Notificaciones y recordatorios.
- Operación completa sin conexión; la instalación en pantalla de inicio del MVP sigue requiriendo internet para consultar y modificar datos.
- Experiencia bilingüe completa; el MVP puede comenzar en español, conservando una estructura preparada para i18n.

### Fuera de alcance

- Red social, rankings, entrenadores externos o multiatleta.
- Diagnóstico médico o autorización médica.
- Ajustes automáticos del plan sin confirmación del atleta.
- APIs privadas de Garmin, almacenamiento de credenciales Garmin o automatización de MFA/CAPTCHA.
- Exportación integral de cuenta Garmin y proveedores terceros como camino principal.
- Comparación directa entre ritmos de cinta y exterior.
- Prescripción por FC o zonas hasta que la señal sea validada.

## 4. Flujos principales

### F1 — Inicio y acceso

1. El atleta accede mediante usuario y contraseña.
2. El sistema resuelve la identidad desde la sesión autenticada, nunca desde un `userId` enviado por el navegador.
3. Todas las consultas y escrituras se restringen al atleta autenticado.
4. El registro público queda deshabilitado por defecto; la cuenta inicial se aprovisiona de forma controlada.

### F2 — Carga histórica

1. El operador selecciona o ejecuta la importación del staging normalizado.
2. El sistema identifica el archivo y la ejecución mediante hash y metadatos.
3. Cada fila se valida y se inserta o reconcilia por clave provisional.
4. Los valores ausentes permanecen nulos.
5. Repetir la misma importación no crea duplicados.
6. El sistema informa insertados, actualizados, omitidos, rechazados y conteo final reconciliado.

### F3 — Ingestión FIT incremental

1. El sincronizador local o el atleta entrega ID Garmin, FIT original y contexto de origen.
2. El backend valida firma, tamaño, CRC/integridad, lectura SDK y SHA-256.
3. Un ID/hash ya conocido se reconoce como repetición segura.
4. Un ID conocido con otro hash, o un enlace ambiguo, entra en cuarentena sin sobrescritura.
5. El procesador extrae resumen, vueltas, eventos, zonas y muestras disponibles.
6. El sistema enlaza con una actividad histórica solo cuando existe una coincidencia única conforme al contrato Garmin.
7. El upsert es transaccional, idempotente y auditable.
8. La carga manual usa exactamente el mismo flujo como respaldo.

### F4 — Planificación y ejecución

1. El atleta consulta el calendario de sesiones planificadas.
2. Cada sesión muestra objetivo, modalidad, distancia/duración, RPE, calentamiento, bloque, recuperación y enfriamiento cuando corresponda.
3. Una sesión de fuerza, movilidad o pliometría muestra sus ejercicios en orden, con series/repeticiones o tiempo, descanso, carga/RPE, una descripción breve y hasta dos imágenes ilustrativas accesibles.
4. Una actividad importada puede enlazarse automáticamente o manualmente con una sesión.
5. El sistema distingue completada según plan, modificada, sustitución válida, no realizada y opcional no realizada.
6. Los kilómetros omitidos no se trasladan automáticamente a otra fecha.

### F5 — Captura subjetiva

1. Después de cada sesión se registra RPE 1–10.
2. Se registran por separado dolor, ubicación, cambio de zancada, fatiga, sueño, recuperación y síntomas.
3. Fuerza, calidad, tirada larga y pliometría permiten respuesta de 24–48 h.
4. Los valores desconocidos permanecen nulos; no se reconstruyen retrospectivamente.

### F6 — Evaluación semanal

1. La aplicación abre una evaluación de lunes a domingo.
2. Precarga plan y actividades realizadas.
3. Calcula P1, P2, P3 y P4; presenta P5 sin score compuesto.
4. Añade contexto complementario únicamente cuando está disponible y es comparable.
5. Determina una propuesta de semáforo verde, amarillo o rojo, dejando visible la evidencia.
6. El atleta confirma una decisión: ejecutar plan, adaptar, reducir o detener y valorar.
7. La evaluación puede quedar provisional hasta conocer la respuesta de 24–48 h.
8. Toda modificación registra observación, evidencia, comparación, interpretación y recomendación.

### F7 — Revisión y tendencias

1. El dashboard muestra semana actual, cumplimiento, volumen, tirada larga, sRPE y seguridad/recuperación.
2. Permite explorar tendencias de 4, 8 y 12 semanas.
3. Separa cinta y exterior.
4. Los ritmos solo se comparan en actividades equivalentes y con contexto visible.
5. Las métricas informativas no presentan una recomendación automática aislada.

## 5. Requerimientos funcionales obligatorios

### Identidad y acceso

- **AUTH-001:** autenticar al atleta mediante usuario y contraseña.
- **AUTH-002:** deshabilitar registro público por defecto.
- **AUTH-003:** derivar la identidad de la sesión autenticada en todas las operaciones.
- **AUTH-004:** impedir endpoints de “todos los usuarios” y acceso por identificadores arbitrarios.
- **AUTH-005:** permitir cierre de sesión, expiración y recuperación segura de acceso.

### Perfil y carreras

- **PRO-001:** consultar y editar perfil deportivo básico, zona horaria y unidades.
- **PRO-002:** registrar antecedentes y restricciones relevantes con acceso privado y auditoría.
- **RACE-001:** crear y consultar carreras con fecha, distancia, lugar, prioridad y estado.
- **RACE-002:** versionar objetivos y conservar motivo/evidencia de cada cambio.

### Plan

- **PLAN-001:** crear versiones de plan con periodo y estado.
- **PLAN-002:** registrar sesiones planificadas con propósito y prescripción estructurada.
- **PLAN-003:** publicar una sola versión vigente sin borrar versiones anteriores.
- **PLAN-004:** registrar adaptaciones semanales sin mutar silenciosamente el plan original.

### Guías de ejercicios

- **EXE-001:** mantener un catálogo versionado de ejercicios con nombre, descripción breve, equipamiento y señales de seguridad relevantes.
- **EXE-002:** asignar ejercicios ordenados a bloques de fuerza, movilidad o pliometría con series, repeticiones o tiempo, descanso, carga/RPE y notas de ejecución cuando correspondan.
- **EXE-003:** asociar de cero a dos imágenes ilustrativas por revisión de ejercicio, con orden, texto alternativo, procedencia y licencia; la instrucción textual debe seguir disponible sin imágenes.

### Actividades e importación

- **ACT-001:** listar, filtrar, ordenar y paginar actividades desde el servidor.
- **ACT-002:** consultar detalle, origen, identificadores, métricas y estado de validación.
- **ACT-003:** conservar duración y ritmo como valores numéricos, no como texto de presentación.
- **ACT-004:** separar hora local y UTC cuando existan; no inventar offset histórico.
- **ACT-005:** conservar cinta y exterior como modalidades diferentes.
- **CSV-001:** importar idempotentemente las 460 filas históricas esperadas.
- **CSV-002:** conservar archivo/ejecución, fila fuente, clave provisional y valores nulos.
- **CSV-003:** reconciliar conteos y exponer errores por fila sin dejar carga parcial.
- **FIT-001:** aceptar FIT por sincronizador local y carga manual.
- **FIT-002:** validar completamente antes de considerar adquirido un archivo.
- **FIT-003:** deduplicar por ID Garmin y SHA-256.
- **FIT-004:** poner en cuarentena colisiones, ambigüedades e integridad fallida.
- **FIT-005:** conservar procedencia y permitir reprocesamiento con otra versión del SDK.
- **FIT-006:** persistir estructuras FIT normalizadas; no guardar el JSON canónico completo como una sola fila.
- **FIT-007:** una ausencia FIT nunca elimina un valor histórico proveniente del CSV.

### Planificado frente a realizado

- **MATCH-001:** enlazar una actividad con cero o una sesión planificada y conservar el método del vínculo.
- **MATCH-002:** proponer enlace automático solo con coincidencia única y reglas documentadas.
- **MATCH-003:** permitir confirmar, cambiar o retirar el vínculo sin borrar la actividad.
- **MATCH-004:** clasificar ejecución mediante los cinco estados de `TRN-003`.

### Captura y evaluación

- **CAP-001:** capturar RPE, dolor, ubicación, fatiga, sueño, recuperación y síntomas.
- **CAP-002:** registrar respuesta de 24–48 h de sesiones clave.
- **CAP-003:** conservar cada componente y cada valor faltante por separado.
- **EVAL-001:** generar evaluaciones semanales a partir de plan, actividades y captura manual.
- **EVAL-002:** calcular P1 por tipo de sesión y mostrar modificaciones/sustituciones aparte.
- **EVAL-003:** calcular P2 en distancia y tiempo con separación cinta/exterior.
- **EVAL-004:** registrar P3 como una observación explícita de tirada larga.
- **EVAL-005:** calcular `sRPE = minutos × RPE` por sesión, modalidad y semana.
- **EVAL-006:** presentar P5 sin compensar dolor con sueño u otra señal favorable.
- **EVAL-007:** soportar cierre provisional y final.
- **EVAL-008:** aplicar precedencia de seguridad verde/amarilla/roja y mostrar sus causas.

### Decisiones, dashboard y salida

- **DEC-001:** registrar la decisión semanal y los cambios exactos resultantes.
- **DEC-002:** conservar evidencia, autor, timestamp y versión afectada.
- **DEC-003:** impedir que una recomendación automática modifique el plan sin confirmación.
- **DASH-001:** mostrar estado actual, siguiente sesión, carga semanal y alertas pendientes.
- **DASH-002:** visualizar P1–P5 y tendencias comparables sin mezclar modalidades.
- **DASH-003:** permitir navegación desde un agregado hasta sus sesiones fuente.
- **EXP-001:** exportar los datos consolidados del atleta en un formato documentado.
- **EXP-002:** permitir solicitar eliminación o archivo de datos, preservando las reglas de auditoría que correspondan.

## 6. Requerimientos no funcionales

### Seguridad y privacidad

- **SEC-001:** aplicar mínimo privilegio y aislamiento por identidad en backend y Supabase/RLS.
- **SEC-002:** una clave `service_role`, si resulta necesaria, vive solo en backend y no sustituye verificaciones de autorización.
- **SEC-003:** secretos solo en variables/secret stores; nunca en archivos versionados.
- **SEC-004:** no registrar contraseñas, tokens, FIT, coordenadas, síntomas ni payloads completos en logs.
- **SEC-005:** restringir CORS a los orígenes configurados.
- **SEC-006:** documentación de API de producción deshabilitada o protegida.
- **SEC-007:** proteger carga de archivos mediante límites de tamaño, tipo, validación y cuarentena.

### Exactitud e integridad

- **DAT-001:** usar tipos numéricos y unidades explícitas para distancia, duración, ritmo y potencia.
- **DAT-002:** representar ausencias con `NULL`, nunca con cero por defecto.
- **DAT-003:** toda importación y reprocesamiento será idempotente y transaccional.
- **DAT-004:** preservar procedencia y conflictos sin sobrescritura silenciosa.
- **DAT-005:** almacenar timestamps en UTC cuando se conozca el instante y conservar la hora local original.

### Operación

- **OPS-001:** health check sin revelar dependencias ni secretos.
- **OPS-002:** logs estructurados con `correlation_id` y sin datos sensibles.
- **OPS-003:** importaciones largas ejecutables como trabajos reanudables con progreso.
- **OPS-004:** estrategia documentada de respaldo, restauración y retención antes de declarar Supabase fuente única.
- **OPS-005:** errores recuperables no obligan a descargar de nuevo un FIT ya validado.

### Costo de publicación

- **COST-001:** todos los elementos necesarios para publicar el MVP usarán planes gratuitos con costo obligatorio de USD 0: repositorio, frontend, backend, autenticación, PostgreSQL y Storage.
- **COST-002:** no se activarán trials, add-ons, dominios pagados, planes con cuota ni upgrades automáticos. Añadir un medio de pago o aceptar un costo requiere una decisión posterior explícita del usuario y una nueva versión del plan.
- **COST-003:** al acercarse a una cuota gratuita, el sistema debe advertir y degradar, pausar o bloquear la operación afectada antes que generar un cargo. La disponibilidad continua no prevalece sobre costo cero.
- **COST-004:** la interfaz y los runbooks deben contemplar arranque en frío, suspensión por inactividad, agotamiento de cuota y restauración manual propios de los planes gratuitos.

### Experiencia y calidad

- **UX-001:** interfaz mobile-first y responsive, verificada en Safari para iPhone y en navegadores de escritorio de PC, sin scroll horizontal en los flujos principales.
- **UX-002:** estados de carga, vacío, error, pendiente y cuarentena comprensibles.
- **UX-003:** unidades métricas y zona horaria configurables; valor inicial `America/Mexico_City`.
- **UX-004:** navegación principal accesible por teclado y contraste suficiente.
- **UX-005:** las imágenes de ejercicios tendrán texto alternativo, carga diferida y dimensiones reservadas; nunca serán el único medio para comunicar la técnica.
- **UX-006:** navegación, tablas/tarjetas, gráficas, formularios y cargas de archivo se adaptarán a touch y teclado, respetarán las safe areas del iPhone y usarán objetivos táctiles de al menos 44 × 44 CSS px.
- **UX-007:** la publicación HTTPS incluirá Web App Manifest, iconos instalables y metadatos compatibles con iPhone. Desde Safari, el atleta podrá usar “Añadir a pantalla de inicio”, abrir el acceso en modo independiente y encontrar instrucciones dentro de Perfil; no se promete operación offline.
- **QLT-001:** antes de cada despliegue, la verificación local debe compilar frontend/backend y ejecutar pruebas y análisis de dependencias. GitHub Actions permanece deshabilitado.
- **QLT-002:** migraciones y políticas de base de datos se versionan y prueban.
- **QLT-003:** no publicar si detección de secretos, pruebas críticas o importación idempotente fallan.

## 7. Política GitHub

### Límite

El futuro repositorio se crea desde `App/`, no desde la raíz `RunningPerformance`. Esta raíz contiene información privada del atleta y artefactos de POC.

### Permitido

- Código, migraciones, configuración de despliegue y documentación sanitizada.
- `.env.example` con nombres de variables y valores ficticios.
- Fixtures sintéticos generados expresamente, sin rutas o métricas reales.
- Esquemas y ejemplos mínimos que no permitan identificar al atleta.
- Ilustraciones de ejercicios propias, generadas o con licencia compatible, sin personas ni datos identificables, junto con su atribución cuando corresponda.

### Prohibido

- FIT/TCX/GPX, exportaciones CSV, JSON canónicos, imágenes del atleta o de Garmin, rutas y capturas reales.
- `PLAN.md`, `STATE.md`, `task.json` y reportes privados del workspace.
- `.env`, claves Supabase/JWT, cookies, estados de Playwright o credenciales Garmin.
- Dumps de base de datos, logs de payloads y archivos de cuarentena.

### Controles antes del primer push

1. Revisar `git status` y el contenido completo del primer commit.
2. Ejecutar detección de secretos y datos personales.
3. Confirmar que los fixtures sean sintéticos.
4. Verificar compilación, pruebas, migraciones y auditoría de dependencias.
5. Definir licencia y política de contribución si el repositorio será público.

Un repositorio privado no se considera una autorización para subir secretos o datos de salud.

## 8. Aceptación del MVP

El MVP se considera funcional cuando:

1. El atleta inicia sesión y ninguna solicitud puede seleccionar otra identidad.
2. El staging histórico se importa dos veces y el segundo intento no agrega duplicados.
3. Las 460 filas esperadas se reconcilian y los ausentes permanecen nulos.
4. Un FIT válido crea o enriquece una actividad; repetirlo no duplica datos.
5. Un ID/hash conflictivo entra en cuarentena y conserva ambos orígenes.
6. El atleta consulta y filtra su historial y abre el detalle con procedencia.
7. Puede relacionar planificado y realizado y registrar P4/P5.
8. Al abrir una sesión de fuerza, movilidad o pliometría se muestran en orden sus ejercicios, prescripción, descripción breve y hasta dos imágenes accesibles cuando existan.
9. Login, calendario/sesión del día, guía de ejercicios, historial, captura subjetiva, dashboard y carga manual funcionan tanto en Safari de iPhone como en Chrome/Edge de PC.
10. La aplicación genera una evaluación semanal P1–P5 y registra una decisión confirmada.
11. Un cambio del plan crea una nueva versión auditable.
12. El dashboard permite rastrear cada agregado hasta sus datos fuente.
13. El repositorio contiene solo datos sintéticos, pasa detección de secretos, builds y pruebas.
14. Existe procedimiento probado de respaldo y restauración.
15. Repositorio, frontend, backend, Auth, PostgreSQL y Storage están publicados con costo obligatorio de USD 0, sin medio de pago ni componente trial; superar una cuota detiene o degrada el servicio en vez de facturar.
16. En un iPhone real, Safari permite añadir la aplicación a la pantalla de inicio con el nombre e icono definidos; al abrirla desde allí usa modo independiente y permite iniciar sesión y completar los flujos principales con conexión.

## 9. Traspaso a APP-002

El modelo de datos deberá representar, sin duplicación ni pérdida de procedencia:

- identidad y perfil;
- carreras y objetivos versionados;
- planes, versiones y sesiones;
- catálogo, revisiones, prescripciones e imágenes de ejercicios;
- actividades históricas e incrementales;
- fuentes, archivos, importaciones, conflictos y cuarentena;
- resúmenes FIT, vueltas, eventos, zonas y muestras;
- vínculos planificado–realizado;
- captura subjetiva;
- métricas/evaluaciones semanales;
- decisiones y ajustes;
- auditoría y exportaciones.

`APP-002` decidirá tablas, claves, relaciones, índices, RLS, partición o retención de muestras y ubicación del binario FIT.

La enmienda `APP-001-v4-2026-08-12` agregó `COST-001` a `COST-004` y el criterio de aceptación 15. La enmienda `APP-001-v5-2026-08-14` agregó `UX-007` y el criterio 16: instalación en pantalla de inicio del iPhone, separada explícitamente de la operación offline. `APP-001-v6-2026-08-14` establece verificación local, sin GitHub Actions ni base alojada de integración. `APP-013` deberá verificar ambos criterios de despliegue en un dispositivo real.
