using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FischMacroCS.Core;

namespace FischMacroCS.Tests;

public class ReportingUiTests
{
    [Fact]
    public void ReportingAndReplayWindowsCanLayOutWithoutLiveGameOrCredentials()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            string root = Path.Combine(Path.GetTempPath(), "fisch-ui-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                var report = new SubmitRecordingDialog(null, new Settings(), root);
                Render(report, "report-preview.png");
                var replay = new ReplayWindow(root);
                Render(replay, "replay-preview.png");
            }
            catch (Exception ex) { failure = ex; }
            finally { Directory.Delete(root, true); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "WPF layout timed out.");
        Assert.Null(failure);
    }
    private static void Render(Window window, string name)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var surface = new Border { Background = window.Background ?? Brushes.White, Child = content, Resources = window.Resources };
        System.Windows.Documents.TextElement.SetForeground(surface, window.Foreground);
        surface.Measure(new Size(1000, 780)); surface.Arrange(new Rect(0, 0, 1000, 780)); surface.UpdateLayout();
        Assert.True(content.ActualWidth > 500); Assert.True(content.ActualHeight > 400);
        string? output = Environment.GetEnvironmentVariable("FISCH_UI_ARTIFACTS");
        if (output != null)
        {
            Directory.CreateDirectory(output);
            var bitmap = new RenderTargetBitmap(1000, 780, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, name)); png.Save(file);
        }
        window.Close();
    }
}
