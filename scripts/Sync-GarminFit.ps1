[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Pair', 'Upload')]
    [string] $Action,

    [string] $PairingToken,

    [string] $FitPath,

    [long] $GarminActivityId,

    [string] $ApiBaseUrl = 'http://127.0.0.1:5080',

    [string] $CredentialTarget
)

$ErrorActionPreference = 'Stop'

if (-not ('RunningPerformanceCredentialStore' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class RunningPerformanceCredentialStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    public static void Save(string target, string userName, string secret)
    {
        IntPtr blob = Marshal.StringToCoTaskMemUni(secret);
        try
        {
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                Comment = "Running Performance FIT upload credential",
                CredentialBlobSize = checked((uint)(secret.Length * sizeof(char))),
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = userName
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(blob);
        }
    }

    public static string Read(string target)
    {
        IntPtr pointer;
        if (!CredRead(target, CredTypeGeneric, 0, out pointer))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            return Marshal.PtrToStringUni(
                credential.CredentialBlob,
                checked((int)credential.CredentialBlobSize / sizeof(char)));
        }
        finally
        {
            CredFree(pointer);
        }
    }
}
'@
}

$baseUri = [Uri]$ApiBaseUrl.TrimEnd('/')
if ([string]::IsNullOrWhiteSpace($CredentialTarget)) {
    $CredentialTarget = "RunningPerformance/FitUpload/$($baseUri.Authority)"
}

if ($Action -eq 'Pair') {
    if ([string]::IsNullOrWhiteSpace($PairingToken)) {
        throw 'PairingToken is required for Action Pair.'
    }

    $response = Invoke-RestMethod `
        -Method Post `
        -Uri "$($baseUri.AbsoluteUri.TrimEnd('/'))/api/v1/sync/pair" `
        -ContentType 'application/json' `
        -Body (@{ pairingToken = $PairingToken } | ConvertTo-Json -Compress)

    [RunningPerformanceCredentialStore]::Save(
        $CredentialTarget,
        [string]$response.clientId,
        [string]$response.credential)

    [pscustomobject]@{
        clientId = $response.clientId
        expiresAt = $response.expiresAt
        scopes = $response.scopes
        credentialTarget = $CredentialTarget
    }
    exit 0
}

if ($GarminActivityId -le 0) {
    throw 'GarminActivityId must be a positive activity ID from the Garmin download context.'
}
if ([string]::IsNullOrWhiteSpace($FitPath)) {
    throw 'FitPath is required for Action Upload.'
}

$resolvedFit = (Resolve-Path -LiteralPath $FitPath).Path
if ([IO.Path]::GetExtension($resolvedFit) -notin @('.fit', '.FIT')) {
    throw 'FitPath must identify a .fit file.'
}

$credential = [RunningPerformanceCredentialStore]::Read($CredentialTarget)
$sha256 = (Get-FileHash -LiteralPath $resolvedFit -Algorithm SHA256).Hash.ToLowerInvariant()
$headers = @{
    Authorization = "FitUpload $credential"
    'Idempotency-Key' = "fit-$GarminActivityId-$sha256"
}
$fileName = [Uri]::EscapeDataString([IO.Path]::GetFileName($resolvedFit))
$accepted = Invoke-RestMethod `
    -Method Post `
    -Uri "$($baseUri.AbsoluteUri.TrimEnd('/'))/api/v1/sync/fit?fileName=$fileName&garminActivityId=$GarminActivityId" `
    -Headers $headers `
    -ContentType 'application/vnd.ant.fit' `
    -InFile $resolvedFit

[pscustomobject]@{
    runId = $accepted.runId
    sourceFileId = $accepted.sourceFileId
    garminActivityId = $accepted.garminActivityId
    sha256 = $accepted.sha256
    status = $accepted.status
    reusedStoredObject = $accepted.reusedStoredObject
    reusedReceipt = $accepted.reusedReceipt
}
