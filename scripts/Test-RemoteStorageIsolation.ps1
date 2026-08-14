[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z]{20}$')]
    [string] $ProjectRef
)

$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
$cliName = if ($IsWindows) { 'supabase.cmd' } else { 'supabase' }
$cliPath = Join-Path $appRoot "src/web/node_modules/.bin/$cliName"

if (-not (Test-Path $cliPath)) {
    throw 'Supabase CLI is not installed. Run npm ci --prefix src/web first.'
}

$keyOutput = @(& $cliPath --workdir $appRoot projects api-keys --project-ref $ProjectRef --output json)
if ($LASTEXITCODE -ne 0) {
    throw 'Supabase CLI could not retrieve the project API keys.'
}

$keys = ($keyOutput -join "`n") | ConvertFrom-Json
$publicKey = @($keys) |
    Where-Object { $_.type -eq 'publishable' -and $_.api_key -notmatch '\*' } |
    Select-Object -First 1 -ExpandProperty api_key

if ([string]::IsNullOrWhiteSpace($publicKey)) {
    $publicKey = @($keys) |
        Where-Object { $_.type -eq 'legacy' -and $_.name -eq 'anon' -and $_.api_key -notmatch '\*' } |
        Select-Object -First 1 -ExpandProperty api_key
}

if ([string]::IsNullOrWhiteSpace($publicKey)) {
    throw 'No usable publishable or legacy anon key was returned.'
}

$adminKey = @($keys) |
    Where-Object { $_.type -eq 'legacy' -and $_.name -eq 'service_role' -and $_.api_key -notmatch '\*' } |
    Select-Object -First 1 -ExpandProperty api_key

if ([string]::IsNullOrWhiteSpace($adminKey)) {
    throw 'No usable administrative key was returned for test-object cleanup.'
}

$apiUrl = "https://$ProjectRef.supabase.co"

function Get-SyntheticAccessToken {
    param(
        [Parameter(Mandatory)] [string] $Email,
        [Parameter(Mandatory)] [string] $Password
    )

    $response = Invoke-RestMethod `
        -Method Post `
        -Uri "$apiUrl/auth/v1/token?grant_type=password" `
        -Headers @{ apikey = $publicKey } `
        -ContentType 'application/json' `
        -Body (@{ email = $Email; password = $Password } | ConvertTo-Json)

    return $response.access_token
}

function Invoke-ExpectedDenial {
    param(
        [Parameter(Mandatory)] [ValidateSet('Get', 'Post')] [string] $Method,
        [Parameter(Mandatory)] [string] $Uri,
        [Parameter(Mandatory)] [hashtable] $Headers,
        [byte[]] $Body
    )

    try {
        $parameters = @{
            Method = $Method
            Uri = $Uri
            Headers = $Headers
            UseBasicParsing = $true
            ErrorAction = 'Stop'
        }
        if ($null -ne $Body) {
            $parameters.ContentType = 'application/octet-stream'
            $parameters.Body = $Body
        }

        Invoke-WebRequest @parameters | Out-Null
        throw "Expected Storage to deny $Method $Uri."
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        if ($statusCode -notin 400, 403, 404) {
            throw
        }
    }
}

$ownerA = '11111111-1111-4111-8111-111111111111'
$ownerB = '22222222-2222-4222-8222-222222222222'
$tokenA = Get-SyntheticAccessToken 'athlete-a@example.invalid' 'synthetic-only-a'
$tokenB = Get-SyntheticAccessToken 'athlete-b@example.invalid' 'synthetic-only-b'
$objectName = "$ownerA/app005/$([guid]::NewGuid().ToString('N')).bin"
$ownObjectUri = "$apiUrl/storage/v1/object/athlete-files/$objectName"
$ownReadUri = "$apiUrl/storage/v1/object/authenticated/athlete-files/$objectName"
$headersA = @{ apikey = $publicKey; Authorization = "Bearer $tokenA" }
$headersB = @{ apikey = $publicKey; Authorization = "Bearer $tokenB" }

$upload = Invoke-WebRequest `
    -Method Post `
    -Uri $ownObjectUri `
    -Headers $headersA `
    -ContentType 'application/octet-stream' `
    -Body ([byte[]](1, 2, 3)) `
    -UseBasicParsing

$download = Invoke-WebRequest -Method Get -Uri $ownReadUri -Headers $headersA -UseBasicParsing
if ($upload.StatusCode -ne 200 -or $download.StatusCode -ne 200) {
    throw 'Owner A could not write and read its own private object.'
}

Invoke-ExpectedDenial -Method Get -Uri $ownReadUri -Headers $headersB

$crossOwnerUri = "$apiUrl/storage/v1/object/athlete-files/$ownerB/app005/$([guid]::NewGuid().ToString('N')).bin"
Invoke-ExpectedDenial -Method Post -Uri $crossOwnerUri -Headers $headersA -Body ([byte[]](4, 5, 6))

$cleanup = Invoke-WebRequest `
    -Method Delete `
    -Uri $ownObjectUri `
    -Headers @{ apikey = $adminKey; Authorization = "Bearer $adminKey" } `
    -UseBasicParsing

if ($cleanup.StatusCode -ne 200) {
    throw 'The synthetic Storage object could not be removed after the test.'
}

$publicKey = $null
$adminKey = $null
$tokenA = $null
$tokenB = $null

Write-Output 'Remote Auth/Storage isolation OK: synthetic sign-in and own upload/read allowed; cross-owner access denied; test object removed.'
