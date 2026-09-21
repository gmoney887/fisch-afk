Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$assetRoot = Join-Path $PSScriptRoot '../Assets'
# A simple hook and wave emblem stays legible in the Windows title bar.
$drawing = [Windows.Media.DrawingGroup]::new()
$dc = $drawing.Open()
$dc.DrawRoundedRectangle([Windows.Media.BrushConverter]::new().ConvertFromString('#102435'), $null, [Windows.Rect]::new(0,0,64,64), 14,14)
$pen = [Windows.Media.Pen]::new([Windows.Media.BrushConverter]::new().ConvertFromString('#67E8F9'), 7)
$pen.StartLineCap = 'Round'; $pen.EndLineCap = 'Round'; $pen.LineJoin = 'Round'
$dc.DrawGeometry($null, $pen, [Windows.Media.Geometry]::Parse('M 39,12 L 39,34 C 39,49 18,49 18,35 L 18,29 M 18,29 L 26,35'))
$dc.DrawEllipse([Windows.Media.Brushes]::White, $null, [Windows.Point]::new(39,12), 4,4)
$wave = [Windows.Media.Pen]::new([Windows.Media.BrushConverter]::new().ConvertFromString('#38BDF8'), 3)
$wave.StartLineCap = 'Round'; $wave.EndLineCap = 'Round'
$dc.DrawGeometry($null, $wave, [Windows.Media.Geometry]::Parse('M 12,53 C 22,48 25,58 34,53 C 42,48 46,55 53,51'))
$dc.Close()
$sizes = @(16,24,32,48,64,128,256)
$images = @()
foreach ($size in $sizes) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    $context.PushTransform([Windows.Media.ScaleTransform]::new($size/64.0,$size/64.0))
    $context.DrawDrawing($drawing); $context.Pop(); $context.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new(); $encoder.Save($stream)
    $images += ,$stream.ToArray(); $stream.Dispose()
}
[IO.File]::WriteAllBytes((Join-Path $assetRoot 'angler.png'), $images[-1])
$file = [IO.File]::Create((Join-Path $assetRoot 'angler.ico'))
$writer = [IO.BinaryWriter]::new($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i=0; $i -lt $sizes.Count; $i++) {
    $dimension = $sizes[$i] % 256
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
$writer.Dispose()
