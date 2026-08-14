[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BackupPath,
    [Parameter(Mandatory)] [switch] $ConfirmIsolatedTarget,
    [switch] $AllowRemoteIsolatedTarget
)

$ErrorActionPreference = 'Stop'
if (!$ConfirmIsolatedTarget) { throw 'La restauración exige -ConfirmIsolatedTarget.' }
if ([string]::IsNullOrWhiteSpace($env:RESTORE_DATABASE_URL)) {
    throw 'RESTORE_DATABASE_URL es obligatorio y debe apuntar a una base vacía y desechable.'
}
if ([string]::IsNullOrWhiteSpace($env:RESTORE_SUPABASE_URL) -or
    [string]::IsNullOrWhiteSpace($env:RESTORE_SUPABASE_SECRET_KEY)) {
    throw 'RESTORE_SUPABASE_URL y RESTORE_SUPABASE_SECRET_KEY son obligatorios para probar Storage.'
}

$databaseUri = [Uri]$env:RESTORE_DATABASE_URL
$storageUri = [Uri]$env:RESTORE_SUPABASE_URL
$localHosts = @('localhost', '127.0.0.1', '::1')
if (!$AllowRemoteIsolatedTarget -and
    ($databaseUri.Host -notin $localHosts -or $storageUri.Host -notin $localHosts)) {
    throw 'Por defecto sólo se permite restaurar en localhost. Usa -AllowRemoteIsolatedTarget únicamente para un proyecto nuevo y desechable.'
}

$credentials = $databaseUri.UserInfo.Split(':', 2)
if ($credentials.Count -ne 2) { throw 'RESTORE_DATABASE_URL debe incluir usuario y contraseña.' }
$databaseUser = [Uri]::UnescapeDataString($credentials[0])
$databasePassword = [Uri]::UnescapeDataString($credentials[1])
$databaseName = $databaseUri.AbsolutePath.TrimStart('/')
if ([string]::IsNullOrWhiteSpace($databaseName)) { $databaseName = 'postgres' }
$psql = Get-Command psql -ErrorAction SilentlyContinue
if ($null -eq $psql) { throw 'psql es obligatorio para la prueba de restauración oficial de Supabase.' }

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("rp-restore-" + [Guid]::NewGuid().ToString('N'))
$systemTemporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$temporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
if (!$temporaryRoot.StartsWith($systemTemporaryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'No se pudo crear un staging temporal acotado.'
}
[IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null

$archivePath = Join-Path $temporaryRoot 'payload.zip'
$payloadRoot = Join-Path $temporaryRoot 'payload'
Import-Module (Join-Path $PSScriptRoot 'BackupCrypto.psm1') -Force
try {
    $sidecar = "$BackupPath.sha256"
    if (Test-Path -LiteralPath $sidecar) {
        $expectedHash = ((Get-Content -LiteralPath $sidecar -Raw).Trim() -split '\s+')[0]
        $actualHash = (Get-FileHash -LiteralPath $BackupPath -Algorithm SHA256).Hash
        if (!$actualHash.Equals($expectedHash, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'El SHA-256 externo del respaldo no coincide.'
        }
    }

    $passphrase = Read-Host 'Frase de cifrado del respaldo' -AsSecureString
    Unprotect-RunningPerformanceBackup -InputPath $BackupPath -OutputPath $archivePath -Passphrase $passphrase
    [IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $payloadRoot)
    $manifest = Get-Content -LiteralPath (Join-Path $payloadRoot 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.billingEnabled -ne $false) {
        throw 'El manifiesto no cumple el contrato de respaldo versión 1 y costo cero.'
    }
    foreach ($file in @($manifest.database)) {
        $path = Join-Path $payloadRoot $file.path
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if (!$hash.Equals($file.sha256, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Falló la integridad de $($file.path)."
        }
    }
    foreach ($file in @($manifest.storage)) {
        $path = Join-Path $payloadRoot $file.backupPath
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if (!$hash.Equals($file.sha256, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Falló la integridad de $($file.backupPath)."
        }
    }

    $previousPassword = $env:PGPASSWORD
    $previousSslMode = $env:PGSSLMODE
    $env:PGPASSWORD = $databasePassword
    $env:PGSSLMODE = if ($databaseUri.Host -in $localHosts) { 'disable' } else { 'require' }
    try {
        $databaseArgs = @(
            '--host', $databaseUri.Host,
            '--port', $databaseUri.Port,
            '--username', $databaseUser,
            '--dbname', $databaseName,
            '--single-transaction',
            '--variable', 'ON_ERROR_STOP=1',
            '--file', (Join-Path $payloadRoot 'database/roles.sql'),
            '--file', (Join-Path $payloadRoot 'database/schema.sql'),
            '--command', 'SET session_replication_role = replica',
            '--file', (Join-Path $payloadRoot 'database/data.sql'))
        & $psql.Source @databaseArgs
        if ($LASTEXITCODE -ne 0) { throw 'La restauración PostgreSQL transaccional falló.' }
    }
    finally {
        $env:PGPASSWORD = $previousPassword
        $env:PGSSLMODE = $previousSslMode
        $databasePassword = $null
    }

    $headers = @{
        apikey = $env:RESTORE_SUPABASE_SECRET_KEY
        Authorization = "Bearer $($env:RESTORE_SUPABASE_SECRET_KEY)"
    }
    foreach ($file in @($manifest.storage)) {
        $segments = $file.objectPath.Split('/', [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { [Uri]::EscapeDataString($_) }
        $objectPath = $segments -join '/'
        $uri = "$($storageUri.AbsoluteUri.TrimEnd('/'))/storage/v1/object/$($manifest.bucket)/$objectPath"
        $sourcePath = Join-Path $payloadRoot $file.backupPath
        Invoke-WebRequest -Uri $uri -Method Post -Headers $headers -InFile $sourcePath -ContentType 'application/octet-stream' | Out-Null
        $verificationPath = Join-Path $temporaryRoot ("verify-" + [Guid]::NewGuid().ToString('N'))
        try {
            Invoke-WebRequest -Uri $uri -Method Get -Headers $headers -OutFile $verificationPath | Out-Null
            $hash = (Get-FileHash -LiteralPath $verificationPath -Algorithm SHA256).Hash
            if (!$hash.Equals($file.sha256, [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Un objeto restaurado no coincide con su SHA-256.'
            }
        }
        finally {
            Remove-Item -LiteralPath $verificationPath -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Output "Restauración aislada verificada: base transaccional y $(@($manifest.storage).Count) objeto(s) privados."
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        $resolvedTemporary = [IO.Path]::GetFullPath($temporaryRoot)
        if (!$resolvedTemporary.StartsWith($systemTemporaryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Se rechazó limpiar un staging fuera del directorio temporal.'
        }
        Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
    }
}
