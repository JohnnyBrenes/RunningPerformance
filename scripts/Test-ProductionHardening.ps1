[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
$program = Get-Content -Raw (Join-Path $appRoot 'src/backend/RunningPerformance.Api/Program.cs')
$worker = Get-Content -Raw (Join-Path $appRoot 'src/backend/RunningPerformance.Infrastructure/Jobs/IngestionWorker.cs')
$vercel = Get-Content -Raw (Join-Path $appRoot 'src/web/vercel.json') | ConvertFrom-Json
$render = Get-Content -Raw (Join-Path $appRoot 'render.yaml')
$gitignore = Get-Content -Raw (Join-Path $appRoot '.gitignore')
$workflowRoot = Join-Path $appRoot '.github/workflows'
$workflowFiles = if (Test-Path -LiteralPath $workflowRoot) {
    @(Get-ChildItem -LiteralPath $workflowRoot -Force -File | Where-Object { $_.Extension -in @('.yml', '.yaml') })
}
else {
    @()
}

if ($program -notmatch 'AddRateLimiter' -or $program -notmatch 'Status429TooManyRequests') {
    throw 'Production API rate limiting is not configured.'
}
if ($program -notmatch 'CORS_ALLOWED_ORIGINS is required in production') {
    throw 'Production CORS does not fail closed.'
}
if ($program -notmatch 'MaxRequestBodySize' -or $program -notmatch 'MaxRequestHeadersTotalSize') {
    throw 'Kestrel request limits are incomplete.'
}
if ($program -notmatch 'IsDevelopment\(\)[\s\S]*MapOpenApi') {
    throw 'OpenAPI must only be exposed in Development.'
}
if ($program -notmatch 'StrictTransportSecurity' -or $program -notmatch 'Content-Security-Policy') {
    throw 'API security headers are incomplete.'
}
if ($program -notmatch 'WorkerHeartbeatHealthCheck' -or $worker -notmatch 'RecordWorkerHeartbeat') {
    throw 'Worker heartbeat is not part of readiness.'
}

$globalHeaders = @($vercel.headers | Where-Object source -eq '/(.*)').headers
$requiredHeaders = @(
    'Content-Security-Policy',
    'Strict-Transport-Security',
    'X-Content-Type-Options',
    'Referrer-Policy',
    'Permissions-Policy',
    'X-Frame-Options')
foreach ($header in $requiredHeaders) {
    if ($globalHeaders.key -notcontains $header) { throw "Vercel no define $header." }
}
$csp = ($globalHeaders | Where-Object key -eq 'Content-Security-Policy').value
foreach ($directive in @("script-src 'self'", "object-src 'none'", "frame-ancestors 'none'", 'upgrade-insecure-requests')) {
    if ($csp -notmatch [regex]::Escape($directive)) { throw "La CSP omite $directive." }
}
if ($render -notmatch '(?m)^\s+plan:\s+free\s*$' -or
    $render -notmatch '(?m)^\s+healthCheckPath:\s+/health/ready\s*$') {
    throw 'Render must remain Free and use dependency readiness.'
}
if ($render -match '(?m)^\s+(disk|numInstances|scaling):') {
    throw 'Render configuration contains a non-free persistence or scaling feature.'
}
if ($workflowFiles.Count -gt 0) {
    throw 'GitHub Actions must remain disabled; all verification is local by project policy.'
}
if ($gitignore -notmatch 'supabase/\.temp/') {
    throw 'Supabase linked-project metadata is not excluded from Git.'
}
foreach ($file in @('BackupCrypto.psm1', 'New-ProductionBackup.ps1', 'Test-ProductionRestore.ps1', 'Test-BackupCrypto.ps1')) {
    if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot $file))) { throw "Falta el artefacto operativo $file." }
}

Write-Output 'Production hardening OK: API, worker, Vercel, Render, local verification and backup contracts are fail-closed.'
