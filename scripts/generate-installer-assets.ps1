param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($output) | Out-Null

$width = 994
$height = 720
$backgroundPath = Join-Path $output "nora-installer-background.png"
$logoPath = Join-Path $output "nora-installer-logo.png"

$bitmap = [System.Drawing.Bitmap]::new($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    $bounds = [System.Drawing.Rectangle]::new(0, 0, $width, $height)
    $base = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $bounds,
        [System.Drawing.Color]::FromArgb(255, 7, 10, 15),
        [System.Drawing.Color]::FromArgb(255, 12, 16, 23),
        15)
    try { $graphics.FillRectangle($base, $bounds) }
    finally { $base.Dispose() }

    # One restrained amber focal field on the left; the right stays quiet for copy.
    for ($radius = 280; $radius -ge 50; $radius -= 10) {
        $alpha = [Math]::Max(0, [Math]::Round(2 + (290 - $radius) * 0.018))
        $glow = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb($alpha, 255, 156, 38))
        try {
            $graphics.FillEllipse($glow, 150 - $radius, 360 - $radius, $radius * 2, $radius * 2)
        }
        finally { $glow.Dispose() }
    }

    $gridPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(18, 255, 156, 38), 1)
    try {
        for ($x = 0; $x -lt 460; $x += 34) { $graphics.DrawLine($gridPen, $x, 420, 220 + ($x * 0.18), 720) }
        for ($y = 438; $y -lt 720; $y += 30) { $graphics.DrawLine($gridPen, 0, $y, 520, $y) }
    }
    finally { $gridPen.Dispose() }

    foreach ($ring in @(112, 150, 198)) {
        $ringPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(34, 255, 156, 38), 1)
        try { $graphics.DrawEllipse($ringPen, 150 - $ring, 300 - $ring, $ring * 2, $ring * 2) }
        finally { $ringPen.Dispose() }
    }

    $accentPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(118, 255, 156, 38), 2)
    $accentPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $accentPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    try {
        $graphics.DrawArc($accentPen, 38, 188, 224, 224, 205, 98)
        $graphics.DrawLine($accentPen, 96, 503, 176, 503)
        $graphics.DrawLine($accentPen, 176, 503, 212, 473)
    }
    finally { $accentPen.Dispose() }

    $fade = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.Rectangle]::new(220, 0, $width - 220, $height),
        [System.Drawing.Color]::FromArgb(20, 7, 10, 15),
        [System.Drawing.Color]::FromArgb(238, 7, 10, 15),
        0)
    try { $graphics.FillRectangle($fade, 220, 0, $width - 220, $height) }
    finally { $fade.Dispose() }

    $bitmap.Save($backgroundPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}

$icon = [System.Drawing.Icon]::new((Join-Path $root "assets\logo.ico"), [System.Drawing.Size]::new(128, 128))
try {
    $logo = $icon.ToBitmap()
    try { $logo.Save($logoPath, [System.Drawing.Imaging.ImageFormat]::Png) }
    finally { $logo.Dispose() }
}
finally { $icon.Dispose() }

Write-Host "INSTALLER ASSETS READY: $backgroundPath; $logoPath"

