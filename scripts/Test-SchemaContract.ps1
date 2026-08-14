[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
$model = Get-Content -Raw (Join-Path $appRoot 'docs/data-model.json') | ConvertFrom-Json
$expected = @($model.tableGroups | ForEach-Object { $_.tables.name } | Sort-Object)
$sql = Get-ChildItem (Join-Path $appRoot 'supabase/migrations') -Filter '*.sql' |
    Sort-Object Name |
    Get-Content -Raw
$actual = @([regex]::Matches(($sql -join "`n"), 'create\s+table\s+app\.([a-z0-9_]+)', 'IgnoreCase') |
    ForEach-Object { $_.Groups[1].Value.ToLowerInvariant() } |
    Sort-Object -Unique)

$missing = @($expected | Where-Object { $_ -notin $actual })
$unexpected = @($actual | Where-Object { $_ -notin $expected })
$views = @([regex]::Matches(($sql -join "`n"), 'create\s+(?:or\s+replace\s+)?view\s+app\.([a-z0-9_]+)', 'IgnoreCase') |
    ForEach-Object { $_.Groups[1].Value.ToLowerInvariant() } |
    Sort-Object -Unique)

if ($expected.Count -ne 45 -or $actual.Count -ne 45 -or $missing.Count -gt 0 -or $unexpected.Count -gt 0) {
    throw "Schema table mismatch. Expected=$($expected.Count), Actual=$($actual.Count), Missing=$($missing -join ','), Unexpected=$($unexpected -join ',')"
}

if ($views.Count -ne 9) {
    throw "Expected 9 app views, found $($views.Count): $($views -join ',')"
}

$sqlText = $sql -join "`n"
foreach ($table in $expected) {
    $listedInLoop = $sqlText -match [regex]::Escape("'$table'")
    $hasExplicitRls = $sqlText -match "alter\s+table\s+app\.$([regex]::Escape($table))\s+enable\s+row\s+level\s+security"
    if (-not $listedInLoop -and -not $hasExplicitRls) {
        throw "Table $table is not included in an RLS policy loop or explicit policy block."
    }
}

Write-Output "Schema contract OK: 45 tables, 9 views, every table included in RLS policy definitions."
