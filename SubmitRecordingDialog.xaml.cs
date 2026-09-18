using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FischMacroCS.Core;
using OpenCvSharp.WpfExtensions;

namespace FischMacroCS;

public sealed record SessionItemViewModel(string DirectoryPath, string DisplayName);

public partial class SubmitRecordingDialog : Window
{
    private readonly Settings _settings;
    private readonly CancellationTokenSource _closing = new();
    private bool _sending;
    public SubmitRecordingDialog(FishingEngine? engine, Settings? settings = null, string? recordingsRoot = null)
    {
        InitializeComponent();
        _settings = settings ?? engine?.Config ?? Settings.Load();
        TxtRepository.Text = _settings.DiagnosticsRepository;
        string root = recordingsRoot ?? engine?.Recorder.RecordingsDirectory ?? AppDataPaths.FilePath("recordings");
        LstSessions.ItemsSource = AppDataPaths.RecordingRoots(root)
            .Where(Directory.Exists).SelectMany(Directory.GetDirectories)
            .Where(d => File.Exists(Path.Combine(d, "summary.txt")))
            .OrderByDescending(Directory.GetCreationTimeUtc)
            .Select(d => new SessionItemViewModel(d, Path.GetFileName(d))).ToArray();
        Closed += (_, _) => _closing.Cancel();
        if (LstSessions.Items.Count > 0) LstSessions.SelectedIndex = 0;
    }

    private ReportMediaOptions Media()
    {
        var parts = TxtMask.Text.Split(',').Select(s => int.TryParse(s.Trim(), out int v) ? v : -1).ToArray();
        if (parts.Length != 4 || parts.Any(v => v < 0)) throw new ArgumentException("Mask must be four non-negative integers: x,y,width,height.");
        return new(LstFrames.SelectedItems.Cast<string>().Order(StringComparer.Ordinal).ToArray(), parts[0], parts[1], parts[2], parts[3]);
    }
    private void SessionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstSessions.SelectedItem is not SessionItemViewModel session) return;
        TxtSummaryPreview.Text = RecordingSubmissionService.SanitizeText(File.ReadAllText(Path.Combine(session.DirectoryPath, "summary.txt")));
        LstFrames.ItemsSource = Directory.GetFiles(session.DirectoryPath, "frame_*.png").Select(Path.GetFileName).Order().ToArray();
        ImgFrame.Source = null;
    }
    private void FrameChanged(object sender, SelectionChangedEventArgs e) => Preview();
    private void MaskChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) Preview(); }
    private void Preview()
    {
        if (LstSessions.SelectedItem is not SessionItemViewModel session || LstFrames.SelectedItem is not string file) { ImgFrame.Source = null; return; }
        try
        {
            using var image = RecordingSubmissionService.LoadReportImage(Path.Combine(session.DirectoryPath, file), Media());
            var bitmap = image.ToBitmapSource(); bitmap.Freeze(); ImgFrame.Source = bitmap;
            TxtStatus.Text = $"{LstFrames.SelectedItems.Count} frames selected.";
        }
        catch (Exception ex) { TxtStatus.Text = ex.Message; }
    }
    private async Task<SubmissionPackageResult> Prepare()
    {
        if (LstSessions.SelectedItem is not SessionItemViewModel session) throw new InvalidOperationException("Select a completed session.");
        var media = Media();
        var result = await Task.Run(() => RecordingSubmissionService.CreateDiagnosticBundle(session.DirectoryPath, media: media));
        if (!result.Success) throw new IOException(result.ErrorMessage);
        return result;
    }
    private async void ExportClick(object sender, RoutedEventArgs e)
    {
        if (_sending) return;
        try
        {
            var result = await Prepare();
            var save = new Microsoft.Win32.SaveFileDialog { Filter = "Diagnostic ZIP|*.zip", FileName = Path.GetFileName(result.ZipPath) };
            if (save.ShowDialog(this) == true) File.Copy(result.ZipPath, save.FileName, true);
            TxtStatus.Text = result.IsReduced ? "Reduced text-only bundle exported. Full original retained locally." : "Export ready.";
        }
        catch (Exception ex) { TxtStatus.Text = ex.Message; }
    }
    private async void SendClick(object sender, RoutedEventArgs e)
    {
        if (_sending) return;
        _sending = true; BtnSend.IsEnabled = false; LstSessions.IsEnabled = LstFrames.IsEnabled = false;
        TxtMask.IsEnabled = TxtDescription.IsEnabled = TxtRepository.IsEnabled = TxtToken.IsEnabled = false;
        try
        {
            string repository = TxtRepository.Text.Trim();
            string token = string.IsNullOrWhiteSpace(TxtToken.Password) ? ReportCredentials.Load(repository) ?? "" : TxtToken.Password;
            if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Enter a repository token for the private destination.");
            string description = RecordingSubmissionService.SanitizeText(TxtDescription.Text);
            var bundle = await Prepare();
            if (_closing.IsCancellationRequested) return;
            string preview = $"{repository}\n{new FileInfo(bundle.ZipPath).Length / 1048576.0:F2} MB\n" +
                (bundle.IsReduced ? "REDUCED: all images and video omitted. Original retained locally.\n" : $"{Media().Frames.Length} selected frames with the previewed mask.\n") +
                "\n" + description + "\n\nSend this report?";
            if (MessageBox.Show(this, preview, "Report preview", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
            if (!string.IsNullOrWhiteSpace(TxtToken.Password)) ReportCredentials.Save(repository, token);
            _settings.DiagnosticsRepository = repository; _settings.Save(); TxtToken.Clear();
            TxtStatus.Text = "Uploading; progress will be saved for retry.";
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            string url = await new PrivateReportClient(http).SendAsync(repository, token, bundle.ZipPath, description, _closing.Token);
            TxtStatus.Text = "Sent: " + url;
        }
        catch (OperationCanceledException) { TxtStatus.Text = "Cancelled. Prepared report and progress retained."; }
        catch (Exception ex) { TxtStatus.Text = ex.Message; }
        finally
        {
            _sending = false; BtnSend.IsEnabled = true; LstSessions.IsEnabled = LstFrames.IsEnabled = true;
            TxtMask.IsEnabled = TxtDescription.IsEnabled = TxtRepository.IsEnabled = TxtToken.IsEnabled = true;
        }
    }
    private void ReplayClick(object sender, RoutedEventArgs e)
    {
        if (LstSessions.SelectedItem is SessionItemViewModel session)
            new ReplayWindow(session.DirectoryPath) { Owner = this }.Show();
    }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}

