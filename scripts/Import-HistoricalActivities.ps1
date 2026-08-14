param(
    [Parameter(Mandatory = $true)]
    [string] $CsvPath,

    [Parameter(Mandatory = $true)]
    [string] $AccessToken,

    [string] $ApiBaseUrl = 'http://127.0.0.1:5080',

    [ValidateRange(1, 1800)]
    [int] $TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'

$resolvedCsv = (Resolve-Path -LiteralPath $CsvPath).Path
if ([IO.Path]::GetExtension($resolvedCsv) -ne '.csv') {
    throw 'CsvPath must identify a .csv file.'
}

$headers = @{ Authorization = "Bearer $AccessToken" }
$safeFileName = [Uri]::EscapeDataString([IO.Path]::GetFileName($resolvedCsv))
$accepted = Invoke-RestMethod `
    -Method Post `
    -Uri "$($ApiBaseUrl.TrimEnd('/'))/api/v1/ingestion-runs/historical-csv?fileName=$safeFileName" `
    -Headers $headers `
    -ContentType 'text/csv' `
    -InFile $resolvedCsv

$deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
do {
    $run = Invoke-RestMethod `
        -Method Get `
        -Uri "$($ApiBaseUrl.TrimEnd('/'))/api/v1/ingestion-runs/$($accepted.runId)" `
        -Headers $headers
    if ($run.status -in @('succeeded', 'failed', 'quarantined', 'cancelled')) {
        break
    }

    Start-Sleep -Seconds 1
} while ([DateTimeOffset]::UtcNow -lt $deadline)

if ($run.status -notin @('succeeded', 'failed', 'quarantined', 'cancelled')) {
    throw "Import $($accepted.runId) did not finish within $TimeoutSeconds seconds."
}

$result = [ordered]@{
    runId = $run.id
    sourceFileId = $run.sourceFileId
    sha256 = $run.sha256
    reusedStoredObject = $accepted.reusedStoredObject
    status = $run.status
    itemCount = $run.itemCount
    successCount = $run.successCount
    failureCount = $run.failureCount
    attemptCount = $run.attemptCount
    errors = $run.errors
}
$result | ConvertTo-Json -Depth 8

if ($run.status -ne 'succeeded') {
    exit 1
}
