$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type 'using System; using System.Runtime.InteropServices; public static class PositioningIconHandle { [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr handle); }'
$bitmap = [Drawing.Bitmap]::new(64,64)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([Drawing.Color]::FromArgb(24,29,37))
$pen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(81,210,226),3)
$graphics.DrawEllipse($pen,14,14,36,36)
$graphics.DrawLine($pen,32,5,32,23)
$graphics.DrawLine($pen,32,41,32,59)
$graphics.DrawLine($pen,5,32,23,32)
$graphics.DrawLine($pen,41,32,59,32)
$brush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(133,245,163))
$graphics.FillEllipse($brush,28,28,8,8)
$handle = $bitmap.GetHicon()
$icon = [Drawing.Icon]::FromHandle($handle)
$stream = [IO.File]::Create((Join-Path $PSScriptRoot 'positioning.ico'))
try { $icon.Save($stream) } finally {
    $stream.Dispose(); $icon.Dispose(); [void][PositioningIconHandle]::DestroyIcon($handle)
    $brush.Dispose(); $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
