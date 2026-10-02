param([int]$Width = 490, [int]$Height = 850, [switch]$Splash)
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$xaml = [IO.File]::ReadAllText((Join-Path $root 'MainWindow.xaml'))
$xaml = $xaml -replace 'x:Class="[^"]+"',''
$xaml = $xaml -replace '\b(Click|Checked|Unchecked|Closing|Collapsed|Expanded|LostFocus|MouseLeftButtonDown|SelectionChanged|TextChanged)="[^"]+"',''
$xaml = $xaml.Replace('Assets/angler.png', (Join-Path $root 'Assets/angler.png')).Replace('Assets/angler.ico', (Join-Path $root 'Assets/angler.ico')).Replace('splash_logo.png', (Join-Path $root 'splash_logo.png'))
$window = [Windows.Markup.XamlReader]::Parse($xaml)
if (-not $Splash) { $window.FindName('OverlaySplash').Visibility = 'Collapsed' }
$window.FindName('TxtAppVersionBadge').Text = 'v1.0.21 PRO'
$content = $window.Content
$window.Content = $null
$content.Resources = $window.Resources
$content.Background = $window.Background
$surface = [Windows.Controls.Grid]::new()
$surface.Background = $window.Background
[void]$surface.Children.Add($content)
$surface.Measure([Windows.Size]::new($Width,$Height))
$surface.Arrange([Windows.Rect]::new(0,0,$Width,$Height))
$surface.UpdateLayout()
$bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($Width,$Height,96,96,[Windows.Media.PixelFormats]::Pbgra32)
$bitmap.Render($surface)
$encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
$out = Join-Path $root "artifacts/ui-refresh-$Width-$Height-$Splash.png"
$stream = [IO.File]::Create($out); $encoder.Save($stream); $stream.Dispose()
Write-Output $out
