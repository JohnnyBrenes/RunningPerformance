Set-StrictMode -Version Latest

$script:Magic = [Text.Encoding]::ASCII.GetBytes('RPBK0001')
$script:Iterations = 310000
$script:SaltLength = 16
$script:IvLength = 16
$script:TagLength = 32
$script:HeaderLength = $script:Magic.Length + 4 + $script:SaltLength + $script:IvLength

function Test-FixedTimeEqual {
    param(
        [Parameter(Mandatory)] [byte[]] $Left,
        [Parameter(Mandatory)] [byte[]] $Right
    )

    if ($Left.Length -ne $Right.Length) { return $false }
    $difference = 0
    for ($index = 0; $index -lt $Left.Length; $index++) {
        $difference = $difference -bor ($Left[$index] -bxor $Right[$index])
    }
    return $difference -eq 0
}

function Get-KeyMaterial {
    param(
        [Parameter(Mandatory)] [Security.SecureString] $Passphrase,
        [Parameter(Mandatory)] [byte[]] $Salt,
        [Parameter(Mandatory)] [int] $Iterations
    )

    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Passphrase)
    $passphraseBytes = $null
    try {
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        if ($plain.Length -lt 14) {
            throw 'La frase de respaldo debe tener al menos 14 caracteres.'
        }

        $passphraseBytes = [Text.Encoding]::UTF8.GetBytes($plain)
        $derive = [Security.Cryptography.Rfc2898DeriveBytes]::new(
            $passphraseBytes,
            $Salt,
            $Iterations,
            [Security.Cryptography.HashAlgorithmName]::SHA256)
        try {
            return $derive.GetBytes(64)
        }
        finally {
            $derive.Dispose()
        }
    }
    finally {
        if ($null -ne $passphraseBytes) {
            [Array]::Clear($passphraseBytes, 0, $passphraseBytes.Length)
        }
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

function Get-FileHmac {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [long] $Length,
        [Parameter(Mandatory)] [byte[]] $Key
    )

    $hmac = [Security.Cryptography.HMACSHA256]::new($Key)
    $stream = [IO.File]::OpenRead($Path)
    try {
        $remaining = $Length
        $buffer = [byte[]]::new(1MB)
        while ($remaining -gt 0) {
            $requested = [int][Math]::Min($buffer.Length, $remaining)
            $read = $stream.Read($buffer, 0, $requested)
            if ($read -le 0) { throw 'El respaldo cifrado terminó antes de lo esperado.' }
            [void]$hmac.TransformBlock($buffer, 0, $read, $null, 0)
            $remaining -= $read
        }
        [void]$hmac.TransformFinalBlock([byte[]]::new(0), 0, 0)
        return $hmac.Hash
    }
    finally {
        $stream.Dispose()
        $hmac.Dispose()
    }
}

function Protect-RunningPerformanceBackup {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $InputPath,
        [Parameter(Mandatory)] [string] $OutputPath,
        [Parameter(Mandatory)] [Security.SecureString] $Passphrase
    )

    if (Test-Path -LiteralPath $OutputPath) { throw "El destino ya existe: $OutputPath" }

    $salt = [Security.Cryptography.RandomNumberGenerator]::GetBytes($script:SaltLength)
    $iv = [Security.Cryptography.RandomNumberGenerator]::GetBytes($script:IvLength)
    $keyMaterial = Get-KeyMaterial $Passphrase $salt $script:Iterations
    $encryptionKey = $keyMaterial[0..31]
    $hmacKey = $keyMaterial[32..63]
    try {
        $header = [byte[]]::new($script:HeaderLength)
        [Array]::Copy($script:Magic, 0, $header, 0, $script:Magic.Length)
        [Array]::Copy([BitConverter]::GetBytes($script:Iterations), 0, $header, $script:Magic.Length, 4)
        [Array]::Copy($salt, 0, $header, $script:Magic.Length + 4, $salt.Length)
        [Array]::Copy($iv, 0, $header, $script:Magic.Length + 4 + $salt.Length, $iv.Length)

        $aes = [Security.Cryptography.Aes]::Create()
        $aes.Key = $encryptionKey
        $aes.IV = $iv
        $aes.Mode = [Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [Security.Cryptography.PaddingMode]::PKCS7
        $input = [IO.File]::OpenRead($InputPath)
        $output = [IO.FileStream]::new(
            $OutputPath,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::ReadWrite,
            [IO.FileShare]::None)
        try {
            $output.Write($header, 0, $header.Length)
            $encryptor = $aes.CreateEncryptor()
            $crypto = [Security.Cryptography.CryptoStream]::new(
                $output,
                $encryptor,
                [Security.Cryptography.CryptoStreamMode]::Write,
                $true)
            try {
                $input.CopyTo($crypto)
                $crypto.FlushFinalBlock()
            }
            finally {
                $crypto.Dispose()
                $encryptor.Dispose()
            }
            $output.Flush($true)
        }
        finally {
            $input.Dispose()
            $output.Dispose()
            $aes.Dispose()
        }

        $authenticatedLength = (Get-Item -LiteralPath $OutputPath).Length
        $tag = Get-FileHmac $OutputPath $authenticatedLength $hmacKey
        $append = [IO.File]::Open($OutputPath, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $append.Write($tag, 0, $tag.Length) } finally { $append.Dispose() }
    }
    catch {
        Remove-Item -LiteralPath $OutputPath -Force -ErrorAction SilentlyContinue
        throw
    }
    finally {
        [Array]::Clear($keyMaterial, 0, $keyMaterial.Length)
        [Array]::Clear($encryptionKey, 0, $encryptionKey.Length)
        [Array]::Clear($hmacKey, 0, $hmacKey.Length)
    }
}

function Unprotect-RunningPerformanceBackup {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $InputPath,
        [Parameter(Mandatory)] [string] $OutputPath,
        [Parameter(Mandatory)] [Security.SecureString] $Passphrase
    )

    if (Test-Path -LiteralPath $OutputPath) { throw "El destino ya existe: $OutputPath" }
    $inputLength = (Get-Item -LiteralPath $InputPath).Length
    if ($inputLength -le ($script:HeaderLength + $script:TagLength)) {
        throw 'El archivo no tiene un formato de respaldo válido.'
    }

    $input = [IO.File]::OpenRead($InputPath)
    try {
        $header = [byte[]]::new($script:HeaderLength)
        if ($input.Read($header, 0, $header.Length) -ne $header.Length) {
            throw 'No se pudo leer el encabezado del respaldo.'
        }

        $magic = $header[0..($script:Magic.Length - 1)]
        if (!(Test-FixedTimeEqual $magic $script:Magic)) {
            throw 'El archivo no es un respaldo Running Performance compatible.'
        }

        $iterations = [BitConverter]::ToInt32($header, $script:Magic.Length)
        if ($iterations -lt 100000 -or $iterations -gt 2000000) { throw 'El costo criptográfico no es válido.' }
        $saltStart = $script:Magic.Length + 4
        $salt = $header[$saltStart..($saltStart + $script:SaltLength - 1)]
        $ivStart = $saltStart + $script:SaltLength
        $iv = $header[$ivStart..($ivStart + $script:IvLength - 1)]

        $input.Seek(-$script:TagLength, [IO.SeekOrigin]::End) | Out-Null
        $storedTag = [byte[]]::new($script:TagLength)
        if ($input.Read($storedTag, 0, $storedTag.Length) -ne $storedTag.Length) {
            throw 'No se pudo leer la autenticación del respaldo.'
        }
    }
    finally {
        $input.Dispose()
    }

    $keyMaterial = Get-KeyMaterial $Passphrase $salt $iterations
    $encryptionKey = $keyMaterial[0..31]
    $hmacKey = $keyMaterial[32..63]
    $cipherPath = "$OutputPath.cipher-$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        $authenticatedLength = $inputLength - $script:TagLength
        $computedTag = Get-FileHmac $InputPath $authenticatedLength $hmacKey
        if (!(Test-FixedTimeEqual $storedTag $computedTag)) {
            throw 'La frase es incorrecta o el respaldo fue alterado.'
        }

        $cipherLength = $inputLength - $script:HeaderLength - $script:TagLength
        $source = [IO.File]::OpenRead($InputPath)
        $cipher = [IO.File]::Create($cipherPath)
        try {
            $source.Seek($script:HeaderLength, [IO.SeekOrigin]::Begin) | Out-Null
            $remaining = $cipherLength
            $buffer = [byte[]]::new(1MB)
            while ($remaining -gt 0) {
                $requested = [int][Math]::Min($buffer.Length, $remaining)
                $read = $source.Read($buffer, 0, $requested)
                if ($read -le 0) { throw 'El cuerpo cifrado está incompleto.' }
                $cipher.Write($buffer, 0, $read)
                $remaining -= $read
            }
        }
        finally {
            $source.Dispose()
            $cipher.Dispose()
        }

        $aes = [Security.Cryptography.Aes]::Create()
        $aes.Key = $encryptionKey
        $aes.IV = $iv
        $aes.Mode = [Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [Security.Cryptography.PaddingMode]::PKCS7
        $cipherInput = [IO.File]::OpenRead($cipherPath)
        $decryptor = $aes.CreateDecryptor()
        $crypto = [Security.Cryptography.CryptoStream]::new(
            $cipherInput,
            $decryptor,
            [Security.Cryptography.CryptoStreamMode]::Read)
        $output = [IO.File]::Create($OutputPath)
        try { $crypto.CopyTo($output) }
        finally {
            $output.Dispose()
            $crypto.Dispose()
            $decryptor.Dispose()
            $cipherInput.Dispose()
            $aes.Dispose()
        }
    }
    catch {
        Remove-Item -LiteralPath $OutputPath -Force -ErrorAction SilentlyContinue
        throw
    }
    finally {
        Remove-Item -LiteralPath $cipherPath -Force -ErrorAction SilentlyContinue
        [Array]::Clear($keyMaterial, 0, $keyMaterial.Length)
        [Array]::Clear($encryptionKey, 0, $encryptionKey.Length)
        [Array]::Clear($hmacKey, 0, $hmacKey.Length)
    }
}

Export-ModuleMember -Function Protect-RunningPerformanceBackup, Unprotect-RunningPerformanceBackup
