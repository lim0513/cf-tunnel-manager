# Renders assets/logo.svg's geometry to a multi-size app.ico and a logo.png preview.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$sizes = 16, 20, 24, 32, 48, 64, 128, 256

function New-LogoBitmap([int]$Size) {
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.ScaleTransform($Size / 256.0, $Size / 256.0)

    $d = 112
    $background = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $background.AddArc(0, 0, $d, $d, 180, 90)
    $background.AddArc(256 - $d, 0, $d, $d, 270, 90)
    $background.AddArc(256 - $d, 256 - $d, $d, $d, 0, 90)
    $background.AddArc(0, 256 - $d, $d, $d, 90, 90)
    $background.CloseFigure()
    $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new(0, 0), [System.Drawing.PointF]::new(0, 256),
        [System.Drawing.ColorTranslator]::FromHtml('#1F4A75'), [System.Drawing.ColorTranslator]::FromHtml('#0E1E33'))
    $g.FillPath($brush, $background)

    $arch = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $arch.AddLine(60, 204, 60, 122)
    $arch.AddArc(60, 54, 136, 136, 180, 180)
    $arch.AddLine(196, 122, 196, 204)
    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 30)
    $pen.StartCap = 'Round'
    $pen.EndCap = 'Round'
    $pen.LineJoin = 'Round'
    $g.DrawPath($pen, $arch)

    $dot = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#F6821F'))
    $g.FillEllipse($dot, 98, 120, 60, 60)
    $g.Dispose()
    $bitmap
}

$frames = foreach ($size in $sizes) {
    $bitmap = New-LogoBitmap $size
    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($size -eq 256) { $bitmap.Save((Join-Path $root 'assets\logo.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bitmap.Dispose()
    , @{ Size = $size; Data = $stream.ToArray() }
}

$output = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($output)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $dimension = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$frame.Data.Length)
    $writer.Write([uint32]$offset)
    $offset += $frame.Data.Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Data) }
$writer.Flush()

$directory = Join-Path $root 'src\CfTunnelManager\Assets'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
[IO.File]::WriteAllBytes((Join-Path $directory 'app.ico'), $output.ToArray())

# Preview: the logo at full size beside the sizes used in the tray and title bar.
$preview = [System.Drawing.Bitmap]::new(420, 280)
$g = [System.Drawing.Graphics]::FromImage($preview)
$g.Clear([System.Drawing.Color]::FromArgb(240, 242, 245))
$large = New-LogoBitmap 256
$g.DrawImage($large, 12, 12, 256, 256)
$y = 12
foreach ($size in 64, 32, 24, 16) {
    $small = New-LogoBitmap $size
    $g.DrawImage($small, 290, $y, $size, $size)
    $y += $size + 12
}
$g.Dispose()
$preview.Save((Join-Path ([IO.Path]::GetTempPath()) 'tunnel-manager-logo-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "ico bytes: $($output.Length)"
