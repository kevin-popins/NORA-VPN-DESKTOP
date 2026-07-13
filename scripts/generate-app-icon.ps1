param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\assets\logo-taskbar.ico")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

# This is intentionally a taskbar-only asset. The supplied r16 logo.ico stays
# untouched for the title bar and system tray, where its detailed ring reads well.
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = [System.Collections.Generic.List[object]]::new()

try {
    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

            # Taskbar at 100% scaling commonly uses 16/20px frames. A flat amber
            # disc and a broad navy N retain the NORA mark but give it the same
            # optical weight as normal desktop app icons.
            $edge = [Math]::Max(0.25, $size * 0.012)
            $disc = [System.Drawing.RectangleF]::new($edge, $edge, $size - $edge * 2, $size - $edge * 2)
            $discBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 255, 156, 38))
            try { $graphics.FillEllipse($discBrush, $disc) }
            finally { $discBrush.Dispose() }

            $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
            try {
                $points = [System.Drawing.PointF[]]@(
                    [System.Drawing.PointF]::new($size * 0.18, $size * 0.18),
                    [System.Drawing.PointF]::new($size * 0.34, $size * 0.18),
                    [System.Drawing.PointF]::new($size * 0.66, $size * 0.63),
                    [System.Drawing.PointF]::new($size * 0.66, $size * 0.18),
                    [System.Drawing.PointF]::new($size * 0.82, $size * 0.18),
                    [System.Drawing.PointF]::new($size * 0.82, $size * 0.82),
                    [System.Drawing.PointF]::new($size * 0.66, $size * 0.82),
                    [System.Drawing.PointF]::new($size * 0.34, $size * 0.37),
                    [System.Drawing.PointF]::new($size * 0.34, $size * 0.82),
                    [System.Drawing.PointF]::new($size * 0.18, $size * 0.82)
                )
                $path.AddPolygon($points)
                $markBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 10, 18, 32))
                try { $graphics.FillPath($markBrush, $path) }
                finally { $markBrush.Dispose() }
            }
            finally { $path.Dispose() }

            $stream = [System.IO.MemoryStream]::new()
            try {
                $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                $frames.Add([PSCustomObject]@{ Size = $size; Bytes = $stream.ToArray() })
            }
            finally { $stream.Dispose() }
        }
        finally {
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }

    $target = [System.IO.Path]::GetFullPath($OutputPath)
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target)) | Out-Null
    $file = [System.IO.File]::Open($target, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $edge = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$edge)
            $writer.Write([byte]$edge)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$frame.Bytes.Length)
            $writer.Write([UInt32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    }
    finally {
        $writer.Dispose()
        $file.Dispose()
    }

    Write-Host "Created taskbar-only icon $target with $($frames.Count) frames."
}
finally {
    $frames.Clear()
}
