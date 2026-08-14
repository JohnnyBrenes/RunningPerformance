[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
$cliName = if ($IsWindows) { 'supabase.cmd' } else { 'supabase' }
$cliPath = Join-Path $appRoot "src/web/node_modules/.bin/$cliName"

if (-not (Test-Path $cliPath)) {
    throw 'Supabase CLI is not installed. Run npm ci --prefix src/web first.'
}

$supabase = & $cliPath --workdir $appRoot status --output json | ConvertFrom-Json

function Get-SyntheticAccessToken {
    param(
        [Parameter(Mandatory)] [string] $Email,
        [Parameter(Mandatory)] [string] $Password
    )

    $response = Invoke-RestMethod `
        -Method Post `
        -Uri "$($supabase.API_URL)/auth/v1/token?grant_type=password" `
        -Headers @{ apikey = $supabase.PUBLISHABLE_KEY } `
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
$ownObjectUri = "$($supabase.API_URL)/storage/v1/object/athlete-files/$objectName"
$ownReadUri = "$($supabase.API_URL)/storage/v1/object/authenticated/athlete-files/$objectName"
$headersA = @{ apikey = $supabase.PUBLISHABLE_KEY; Authorization = "Bearer $tokenA" }
$headersB = @{ apikey = $supabase.PUBLISHABLE_KEY; Authorization = "Bearer $tokenB" }

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

$crossOwnerUri = "$($supabase.API_URL)/storage/v1/object/athlete-files/$ownerB/app005/$([guid]::NewGuid().ToString('N')).bin"
Invoke-ExpectedDenial -Method Post -Uri $crossOwnerUri -Headers $headersA -Body ([byte[]](4, 5, 6))

Write-Output 'Storage isolation OK: own upload/read allowed; other-owner read and cross-owner upload denied.'
