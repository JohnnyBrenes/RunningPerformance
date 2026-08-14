[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$appRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputDirectory = [System.IO.Path]::GetFullPath((Join-Path $appRoot 'src\web\public\icons'))

if (-not $outputDirectory.StartsWith($appRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'The icon output directory must remain inside App/.'
}

[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

function New-WebAppIcon {
    param(
        [Parameter(Mandatory)] [int] $Size,
        [Parameter(Mandatory)] [string] $FileName,
        [Parameter(Mandatory)] [double] $MarkScale
    )

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $graphics.Clear([System.Drawing.ColorTranslator]::FromHtml('#071b18'))

        $markSize = [single]($Size * $MarkScale)
        $markOffset = [single](($Size - $markSize) / 2)
        $markBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#b8efb2'))
        $textBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#123c31'))
        $font = [System.Drawing.Font]::new('Georgia', [single]($Size * 0.45), [System.Drawing.FontStyle]::Bold -bor [System.Drawing.FontStyle]::Italic, [System.Drawing.GraphicsUnit]::Pixel)
        $format = [System.Drawing.StringFormat]::new()

        try {
            $graphics.FillEllipse($markBrush, $markOffset, $markOffset, $markSize, $markSize)
            $format.Alignment = [System.Drawing.StringAlignment]::Center
            $format.LineAlignment = [System.Drawing.StringAlignment]::Center
            $textBounds = [System.Drawing.RectangleF]::new(0, [single]($Size * 0.015), $Size, $Size)
            $graphics.DrawString('R', $font, $textBrush, $textBounds, $format)
        }
        finally {
            $format.Dispose()
            $font.Dispose()
            $textBrush.Dispose()
            $markBrush.Dispose()
        }

        $targetPath = Join-Path $outputDirectory $FileName
        $targetStream = [System.IO.File]::Open($targetPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        try {
            $bitmap.Save($targetStream, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $targetStream.Dispose()
        }
        Write-Output $targetPath
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

New-WebAppIcon -Size 180 -FileName 'apple-touch-icon-180-v1.png' -MarkScale 0.62
New-WebAppIcon -Size 192 -FileName 'app-icon-192-v1.png' -MarkScale 0.62
New-WebAppIcon -Size 512 -FileName 'app-icon-512-v1.png' -MarkScale 0.62
New-WebAppIcon -Size 512 -FileName 'app-icon-maskable-512-v1.png' -MarkScale 0.54
