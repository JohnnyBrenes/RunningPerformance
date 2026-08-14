[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BackupDirectory,
    [string] $Bucket = 'athlete-files'
)

$ErrorActionPreference = 'Stop'
$appRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$backupRoot = [IO.Path]::GetFullPath($BackupDirectory)
$appPrefix = $appRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if ($backupRoot.Equals($appRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $backupRoot.StartsWith($appPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'El respaldo debe guardarse fuera de App y, por tanto, fuera de Git.'
}

[IO.Directory]::CreateDirectory($backupRoot) | Out-Null
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("rp-backup-" + [Guid]::NewGuid().ToString('N'))
$systemTemporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$temporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
if (!$temporaryRoot.StartsWith($systemTemporaryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'No se pudo crear un staging temporal acotado.'
}

$payloadRoot = Join-Path $temporaryRoot 'payload'
$databaseRoot = Join-Path $payloadRoot 'database'
$storageRoot = Join-Path $payloadRoot 'storage'
[IO.Directory]::CreateDirectory($databaseRoot) | Out-Null
[IO.Directory]::CreateDirectory($storageRoot) | Out-Null

$timestamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$archivePath = Join-Path $temporaryRoot 'payload.zip'
$outputPath = Join-Path $backupRoot "running-performance-$timestamp.rpbak"
$cli = Join-Path $appRoot 'src/web/node_modules/.bin/supabase.cmd'
if (!(Test-Path -LiteralPath $cli)) { throw 'Ejecuta npm ci en src/web antes de crear el respaldo.' }

Import-Module (Join-Path $PSScriptRoot 'BackupCrypto.psm1') -Force
$previousTelemetry = $env:SUPABASE_TELEMETRY_DISABLED
$env:SUPABASE_TELEMETRY_DISABLED = '1'
try {
    Push-Location $appRoot
    try {
        & $cli db dump --linked --file (Join-Path $databaseRoot 'roles.sql') --role-only
        if ($LASTEXITCODE -ne 0) { throw 'Falló el dump de roles.' }
        & $cli db dump --linked --file (Join-Path $databaseRoot 'schema.sql')
        if ($LASTEXITCODE -ne 0) { throw 'Falló el dump de esquema.' }
        & $cli db dump --linked --file (Join-Path $databaseRoot 'data.sql') --use-copy --data-only -x 'storage.buckets_vectors' -x 'storage.vector_indexes'
        if ($LASTEXITCODE -ne 0) { throw 'Falló el dump de datos.' }
        & $cli storage cp -r "ss:///$Bucket" $storageRoot --experimental --linked
        if ($LASTEXITCODE -ne 0) { throw 'Falló la copia privada de Storage.' }
    }
    finally {
        Pop-Location
    }

    $databaseFiles = Get-ChildItem -LiteralPath $databaseRoot -File | ForEach-Object {
        [ordered]@{
            path = [IO.Path]::GetRelativePath($payloadRoot, $_.FullName).Replace('\', '/')
            bytes = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $storageFiles = Get-ChildItem -LiteralPath $storageRoot -Recurse -File | ForEach-Object {
        $backupPath = [IO.Path]::GetRelativePath($payloadRoot, $_.FullName).Replace('\', '/')
        $objectPath = [IO.Path]::GetRelativePath($storageRoot, $_.FullName).Replace('\', '/')
        if ($objectPath.StartsWith("$Bucket/", [StringComparison]::OrdinalIgnoreCase)) {
            $objectPath = $objectPath.Substring($Bucket.Length + 1)
        }
        [ordered]@{
            backupPath = $backupPath
            objectPath = $objectPath
            bytes = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $manifest = [ordered]@{
        schemaVersion = 1
        createdAt = [DateTimeOffset]::UtcNow.ToString('O')
        source = 'linked-supabase-production-read-only'
        billingEnabled = $false
        bucket = $Bucket
        database = @($databaseFiles)
        storage = @($storageFiles)
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $payloadRoot 'manifest.json') -Encoding utf8NoBOM

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory(
        $payloadRoot,
        $archivePath,
        [IO.Compression.CompressionLevel]::Optimal,
        $false)

    $passphrase = Read-Host 'Frase de cifrado del respaldo (mínimo 14 caracteres)' -AsSecureString
    Protect-RunningPerformanceBackup -InputPath $archivePath -OutputPath $outputPath -Passphrase $passphrase
    $outputHash = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$outputHash  $([IO.Path]::GetFileName($outputPath))" | Set-Content -LiteralPath "$outputPath.sha256" -Encoding ascii
    Write-Output "Respaldo cifrado creado fuera de Git: $outputPath"
    Write-Output "Storage incluido: $(@($storageFiles).Count) objeto(s)."
}
finally {
    $env:SUPABASE_TELEMETRY_DISABLED = $previousTelemetry
    if (Test-Path -LiteralPath $temporaryRoot) {
        $resolvedTemporary = [IO.Path]::GetFullPath($temporaryRoot)
        if (!$resolvedTemporary.StartsWith($systemTemporaryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Se rechazó limpiar un staging fuera del directorio temporal.'
        }
        Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
    }
}
