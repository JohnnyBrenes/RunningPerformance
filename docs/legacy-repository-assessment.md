# Evaluación de `JohnnyBrenes/RunningProject` como insumo

**Repositorio:** <https://github.com/JohnnyBrenes/RunningProject>  
**Commit revisado:** `55a9defa447bad6296a691711bf9b4139b6e5a4e` del 2026-07-15  
**Decisión:** reutilización selectiva; no migración directa ni copia completa

## Conclusión

El repositorio sí sirve como insumo porque demuestra una aplicación funcional con React, C#/.NET y Supabase, y ya contiene patrones de navegación, filtros, gráficas, entrada manual, autenticación y despliegue. Sin embargo, el nuevo producto requiere otro modelo de datos, autorización estricta, importaciones auditables, FIT y evaluaciones semanales. La base actual debe tratarse como prototipo de experiencia y cantera de componentes, no como arquitectura objetivo.

## Evidencia técnica

- Backend: la compilación Release terminó con cero errores y cuatro warnings.
- Frontend: `npm ci` terminó y `npm run build` produjo el bundle de producción.
- El bundle JavaScript principal fue de aproximadamente 821 kB antes de gzip; requiere división por rutas o funcionalidades.
- `npm run lint` falla porque el script existe pero no hay configuración ESLint.
- No hay pruebas automatizadas ni migraciones SQL versionadas.
- El backend compiló con una advertencia de vulnerabilidad alta en la resolución de `Microsoft.OpenApi`; debe actualizarse y verificarse antes de reutilizar dependencias.
- README y Docker hablan de .NET 8 en algunos comentarios, mientras el proyecto y las imágenes apuntan a .NET 10; existe deriva documental.

## Qué puede reutilizarse después de revisión

| Área | Valor como insumo | Condición |
|---|---|---|
| React + Vite + Tailwind | Alto | Actualizar dependencias, añadir pruebas, routing y separación por funcionalidades |
| Layout responsive y navegación | Alto | Adaptar módulos a plan, actividades, evaluación y decisiones |
| i18n español/inglés | Medio-alto | Mantener estructura y completar accesibilidad |
| Tabla, filtros, orden y exportación | Medio | Mover paginación/filtros al servidor y usar el nuevo contrato de actividad |
| Chart.js y componentes de gráficas | Medio | Recalcular agregados en backend y evitar promedios de ritmo no ponderados |
| Formularios y estados de carga/error | Medio | Reutilizar patrones visuales, no DTOs actuales |
| ASP.NET Controllers/Services/DI | Medio | Conservar la separación conceptual, rehacer autorización, errores y persistencia |
| Health check y Dockerfile | Medio | Actualizar hosting, configuración y hardening |
| Supabase client | Bajo-medio | APP-003 decidirá SDK/acceso; nunca confiar solo en `service_role` |

## Qué no debe heredarse

### Modelo de datos

`Trainnings` solo representa fecha, kilómetros, tiempo, ritmo, tenis, ubicación y un `UserId` textual. Tiempo y ritmo se almacenan como strings. No puede representar procedencia, valores nulos correctos, identidad Garmin, FIT, sesiones planificadas, importaciones, vueltas/muestras, evaluación o auditoría. `APP-002` debe diseñar un esquema nuevo.

### Autorización

Aunque los controladores usan `[Authorize]`, aceptan `userId` desde la URL o el body; cualquier usuario autenticado puede solicitar datos de otro identificador. También existen endpoints para listar todos los entrenamientos/usuarios y eliminar por ID. Como el backend usa `service_role` y omite RLS, este patrón es incompatible con el nuevo requisito de aislamiento. La identidad debe salir exclusivamente del token/sesión y cada operación debe verificar propiedad.

### Autenticación y secretos

El repositorio implementa usuarios y JWT propios. `appsettings.json` está versionado y contiene una clave JWT no ficticia, por lo que debe considerarse expuesta al estar el repositorio público. Antes de reutilizar el despliegue hay que rotarla y retirar secretos del código y del historial operativo. El registro público tampoco es necesario para una aplicación de un atleta.

### Garmin

`docs/sincronizar-plan.md` explora endpoints no oficiales y almacenamiento de contraseña Garmin. Esa propuesta contradice las decisiones vigentes: la adquisición seguirá siendo local, reutilizará sesión interactiva, no guardará credenciales ni automatizará MFA, y conservará carga manual FIT como respaldo. El documento histórico no es un plan de implementación.

### API y operación

- CORS permite cualquier origen, método y header.
- OpenAPI/Scalar queda expuesto en todos los entornos.
- Servicios registran detalles completos de entrenamientos y payloads ante errores.
- Borrado de actividades es físico y carece de auditoría.
- Filtros anuales recuperan primero todos los datos del usuario y filtran en memoria.
- No hay migraciones, pruebas ni CI verificable en el árbol revisado.
- Hay archivos `.DS_Store` y `.vs` versionados y no existe licencia.

## Estrategia recomendada

1. Mantener `RunningProject` como referencia o archivarlo cuando exista reemplazo.
2. Diseñar primero el esquema nuevo en `APP-002` y la arquitectura en `APP-003`.
3. Crear la nueva aplicación en el límite limpio `App/`.
4. Reutilizar componentes visuales uno por uno, con pruebas, en vez de copiar `Frontend` completo.
5. Reescribir autenticación, controladores, servicios y modelos contra los nuevos contratos.
6. Importar datos desde el staging y FIT aprobados, no desde las tablas simples del prototipo salvo que `APP-002` defina una migración explícita.
7. Publicar en un repositorio nuevo o en una historia limpia después de rotar secretos y pasar los controles de GitHub.

## Veredicto

**Sí sirve**, principalmente para ahorrar trabajo de interfaz y aportar experiencia de despliegue. Su reutilización estimada es conceptual y por componentes, no por porcentaje de líneas. El nuevo núcleo de datos, seguridad e ingestión debe construirse de nuevo.

