[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
$render = Get-Content -Raw (Join-Path $appRoot 'render.yaml')
$docker = Get-Content -Raw (Join-Path $appRoot 'Dockerfile')
$package = Get-Content -Raw (Join-Path $appRoot 'src/web/package.json') | ConvertFrom-Json
$vercel = Get-Content -Raw (Join-Path $appRoot 'src/web/vercel.json') | ConvertFrom-Json
$program = Get-Content -Raw (Join-Path $appRoot 'src/backend/RunningPerformance.Api/Program.cs')

$renderPlans = @([regex]::Matches($render, '(?m)^\s+plan:\s+([^\s#]+)\s*$') | ForEach-Object { $_.Groups[1].Value })
if ($renderPlans.Count -eq 0 -or ($renderPlans | Where-Object { $_ -ne 'free' }).Count -gt 0) {
    throw "Every Render plan must be free. Found: $($renderPlans -join ', ')"
}
if ($docker -notmatch '(?m)^USER \$APP_UID\s*$') { throw 'Backend container does not run as the platform non-root user.' }
if (([regex]::Matches($docker, '(?m)^FROM\s+\S+@sha256:[0-9a-f]{64}(?:\s+AS\s+\w+)?\s*$')).Count -ne 2) {
    throw 'Both .NET container stages must be pinned by immutable SHA-256 digest.'
}
if ($package.devDependencies.supabase -ne '2.110.0') { throw 'Supabase CLI is not pinned to 2.110.0.' }
if ($package.packageManager -ne 'npm@11.6.2') { throw 'npm version is not pinned.' }
if ($vercel.framework -ne 'vite') { throw 'Vercel must publish only the static Vite SPA.' }
if ($program -notmatch '(?s)new ServiceStatus\(.+?false,\s*limits\)') { throw 'API status does not prove billing is disabled.' }

$allNpmVersions = @($package.dependencies.PSObject.Properties.Value) + @($package.devDependencies.PSObject.Properties.Value)
$nonExactVersions = @($allNpmVersions | Where-Object { $_ -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$' })
if ($nonExactVersions.Count -gt 0) {
    throw "Direct npm versions must be exact. Found: $($nonExactVersions -join ', ')"
}

Write-Output 'Free deployment contract OK: Render Free, Vercel static SPA, billing disabled, non-root container and exact toolchain.'
