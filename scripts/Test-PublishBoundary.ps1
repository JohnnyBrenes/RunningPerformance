[CmdletBinding()]
param(
    [switch] $GitIndex
)

$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
$ignoredDirectories = @('node_modules', 'bin', 'obj', 'dist', 'coverage', 'TestResults', '.temp')
$forbiddenExtensions = @('.fit', '.tcx', '.gpx', '.csv', '.dump', '.bak', '.pfx', '.p12', '.pem', '.key')

if ($GitIndex) {
    $insideWorkTree = git -C $appRoot rev-parse --is-inside-work-tree 2>$null
    if ($LASTEXITCODE -ne 0 -or $insideWorkTree -ne 'true') {
        throw '-GitIndex requires App to be an initialized Git worktree.'
    }
    $files = @(git -C $appRoot ls-files --cached | ForEach-Object {
        Get-Item -LiteralPath (Join-Path $appRoot $_)
    })
}
else {
    $files = Get-ChildItem $appRoot -Recurse -File | Where-Object {
        $relative = [IO.Path]::GetRelativePath($appRoot, $_.FullName)
        -not ($ignoredDirectories | Where-Object { $relative -match "(^|[\\/])$([regex]::Escape($_))([\\/]|$)" })
    }
}

$forbidden = @($files | Where-Object {
    $_.Extension.ToLowerInvariant() -in $forbiddenExtensions -or
    $_.Name -match 'storage-state|cookie|run-status' -or
    ($_.Name -like '.env*' -and $_.Name -ne '.env.example')
})

if ($forbidden.Count -gt 0) {
    throw "Forbidden publishable files: $($forbidden.FullName -join ', ')"
}

$secretPatterns = @(
    '(?im)(garmin[_-]?(password|token)|supabase[_-]?(service[_-]?role|secret[_-]?key))\s*[:=]\s*["'']?(?!<|synthetic|$)[A-Za-z0-9._-]{16,}',
    '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----'
)
$secretHits = @($files | Where-Object { $_.Length -lt 2MB } | Select-String -Pattern $secretPatterns)
if ($secretHits.Count -gt 0) {
    throw "Potential secrets detected: $((($secretHits.Path | Sort-Object -Unique) -join ', '))"
}

Write-Output "Publish boundary OK: no athlete files, dumps, local secrets or private keys detected."
