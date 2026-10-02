Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$assetRoot = Join-Path $PSScriptRoot '../Assets'
# Render the editable vector master with WPF; no bitmap tracing or external tools.
$mark = [Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText((Join-Path $assetRoot 'angler-mark.xaml')))
$mark.Measure([Windows.Size]::new(256,256))
$mark.Arrange([Windows.Rect]::new(0,0,256,256))
$mark.UpdateLayout()
$drawing = [Windows.Media.VisualBrush]::new($mark)
$sizes = @(16,24,32,48,64,128,256)
$images = @()
foreach ($size in $sizes) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    $context.PushTransform([Windows.Media.ScaleTransform]::new($size/256.0,$size/256.0))
    $context.DrawRectangle($drawing, $null, [Windows.Rect]::new(0,0,256,256)); $context.Pop(); $context.Close()
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

# Legacy resource names remain synchronized so every entry point uses the new identity.
Copy-Item (Join-Path $assetRoot 'angler.png') (Join-Path $assetRoot '../app_icon.png')
Copy-Item (Join-Path $assetRoot 'angler.png') (Join-Path $assetRoot '../splash_logo.png')
Copy-Item (Join-Path $assetRoot 'angler.ico') (Join-Path $assetRoot '../app_icon.ico')
