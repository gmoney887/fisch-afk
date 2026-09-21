param([int]$Width = 490)
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$xaml = [IO.File]::ReadAllText((Join-Path $root 'MainWindow.xaml'))
$xaml = $xaml -replace 'x:Class="[^"]+"',''
$xaml = $xaml -replace '\b(Click|Checked|Unchecked|Closing|Collapsed|Expanded|LostFocus|MouseLeftButtonDown|SelectionChanged|TextChanged)="[^"]+"',''
$xaml = $xaml.Replace('Assets/angler.png', (Join-Path $root 'Assets/angler.png')).Replace('Assets/angler.ico', (Join-Path $root 'Assets/angler.ico')).Replace('splash_logo.png', (Join-Path $root 'splash_logo.png'))
$window = [Windows.Markup.XamlReader]::Parse($xaml)
$window.FindName('OverlaySplash').Visibility = 'Collapsed'
$window.FindName('TxtAppVersionBadge').Text = 'v1.0.12-preview.1 PRO'
$content = $window.Content
$window.Content = $null
$content.Resources = $window.Resources
$content.Background = $window.Background
$content.Width = $Width - 20; $content.Height = 1000
$content.Measure([Windows.Size]::new($Width,1020))
$content.Arrange([Windows.Rect]::new(0,0,$Width,1020))
$content.UpdateLayout()
$bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($Width,1020,96,96,[Windows.Media.PixelFormats]::Pbgra32)
$bitmap.Render($content)
$encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
$out = Join-Path $root "artifacts/ui-refresh-$Width.png"
$stream = [IO.File]::Create($out); $encoder.Save($stream); $stream.Dispose()
Write-Output $out
