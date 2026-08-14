[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("rp-crypto-test-" + [Guid]::NewGuid().ToString('N'))
$temporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
$systemTemporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if (!$temporaryRoot.StartsWith($systemTemporaryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'No se pudo crear un staging temporal acotado.'
}
[IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
Import-Module (Join-Path $PSScriptRoot 'BackupCrypto.psm1') -Force
try {
    $source = Join-Path $temporaryRoot 'synthetic.bin'
    $encrypted = Join-Path $temporaryRoot 'synthetic.rpbak'
    $restored = Join-Path $temporaryRoot 'restored.bin'
    [IO.File]::WriteAllBytes($source, [Security.Cryptography.RandomNumberGenerator]::GetBytes(2MB + 137))
    $passphrase = ConvertTo-SecureString 'synthetic-passphrase-for-local-test' -AsPlainText -Force
    Protect-RunningPerformanceBackup $source $encrypted $passphrase
    Unprotect-RunningPerformanceBackup $encrypted $restored $passphrase
    if ((Get-FileHash $source -Algorithm SHA256).Hash -ne (Get-FileHash $restored -Algorithm SHA256).Hash) {
        throw 'El round-trip cifrado no preservó el payload.'
    }

    $bytes = [IO.File]::ReadAllBytes($encrypted)
    $bytes[[Math]::Floor($bytes.Length / 2)] = $bytes[[Math]::Floor($bytes.Length / 2)] -bxor 0x01
    [IO.File]::WriteAllBytes($encrypted, $bytes)
    $tampered = Join-Path $temporaryRoot 'tampered.bin'
    try {
        Unprotect-RunningPerformanceBackup $encrypted $tampered $passphrase
        throw 'Un respaldo alterado fue aceptado incorrectamente.'
    }
    catch {
        if ($_.Exception.Message -notmatch 'alterado') { throw }
    }
    Write-Output 'Backup crypto OK: round-trip válido y alteración rechazada.'
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
