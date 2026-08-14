# Contribuir

Este repositorio es público, pero la aplicación procesa datos privados de entrenamiento y salud. Toda contribución debe usar únicamente datos sintéticos y conservar el límite publicable descrito en el README.

## Antes de abrir un cambio

- No incluyas actividades reales, rutas GPS, identificadores Garmin, capturas privadas, cookies, tokens, claves, archivos `.env`, dumps ni respaldos.
- Usa dominios reservados como `example.invalid` y credenciales marcadas explícitamente como sintéticas.
- No registres cuerpos, query strings, cabeceras de autorización ni datos del atleta en logs o telemetría.
- Mantén los lockfiles y no reduzcas las protecciones de RLS, autenticación, CORS, CSP, límites de petición o cuotas gratuitas.
- Las ilustraciones deben ser propias, generadas o tener licencia compatible; documenta autor, procedencia y licencia.

## Verificación mínima

Desde la raíz del repositorio:

```powershell
dotnet restore RunningPerformance.slnx --locked-mode
dotnet build RunningPerformance.slnx --configuration Release --no-restore
dotnet test RunningPerformance.slnx --configuration Release --no-build
npm ci --prefix src/web
npm --prefix src/web run lint
npm --prefix src/web test
npm --prefix src/web run build
pwsh ./scripts/Test-SchemaContract.ps1
pwsh ./scripts/Test-PublishBoundary.ps1
pwsh ./scripts/Test-FreeDeployment.ps1
pwsh ./scripts/Test-ProductionHardening.ps1
pwsh ./scripts/Test-BackupCrypto.ps1
```

Los cambios de migraciones también deben pasar `npm --prefix src/web run db:lint` y `npm --prefix src/web run db:test`. Los cambios de flujos visibles deben pasar Playwright en Chromium y WebKit.

## Reporte de seguridad

No publiques vulnerabilidades explotables ni secretos en un issue. Contacta al mantenedor de forma privada y rota cualquier credencial que pudiera haberse expuesto; eliminarla del último commit no basta.

Al contribuir aceptas publicar tu cambio bajo la [licencia MIT](LICENSE).
