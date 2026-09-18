using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FischMacroCS.Core;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace FischMacroCS;

public sealed class ReplayWindow : System.Windows.Window
{
    private readonly ReplaySession _session;
    private readonly ListBox _frames = new() { Width = 210, DisplayMemberPath = "File" };
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBox _state = new() { Text = "Unknown" };
    private readonly ComboBox _outcome = new() { ItemsSource = Enum.GetNames<ActionOutcome>(), SelectedIndex = 0 };
    private readonly TextBox _target = new() { Text = "" };
    private readonly TextBox _bounds = new() { Text = "0,0,0,0" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    public ReplayWindow(string directory)
    {
        _session = new ReplaySession(directory);
        Title = "Replay and label — " + _session.SessionId; Width = 1000; Height = 720;
        var root = new DockPanel { Margin = new Thickness(14) }; Content = root;
        var controls = new StackPanel(); DockPanel.SetDock(controls, Dock.Bottom); root.Children.Add(controls);
        controls.Children.Add(_status);
        void Field(string caption, Control control) { controls.Children.Add(new TextBlock { Text = caption }); controls.Children.Add(control); }
        Field("Observed state", _state); Field("Observed outcome", _outcome);
        Field("Target name (optional)", _target); Field("Target bounds in raw image pixels: x,y,width,height", _bounds);
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; controls.Children.Add(actions);
        void Button(string caption, Action action)
        {
            var button = new Button { Content = caption, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(4) };
            button.Click += (_, _) => { try { action(); } catch (Exception ex) { _status.Text = ex.Message; } }; actions.Children.Add(button);
        }
        Button("Save label", () => { _session.SaveLabel(Selected(), Label()); _status.Text = "Label saved."; });
        Button("Export fixture", () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Regression fixture destination" };
            if (dialog.ShowDialog(this) == true) { _session.ExportFixture(Selected(), Label(), dialog.FolderName); _status.Text = "Fixture exported with PC/session provenance."; }
        });
        Button("Compare / save detector result", () =>
        {
            var frame = Selected(); var prediction = _session.Predict(frame);
            string path = Path.Combine(_session.DirectoryPath, frame.File + ".prediction.json");
            string previous = File.Exists(path) ? File.ReadAllText(path) : "No previous detector result.";
            string current = JsonSerializer.Serialize(prediction);
            _status.Text = "Previous: " + previous + "\nCurrent: " + current;
            File.WriteAllText(path, current);
        });
        DockPanel.SetDock(_frames, Dock.Left); root.Children.Add(_frames); root.Children.Add(_image);
        _frames.ItemsSource = _session.Frames;
        _frames.SelectionChanged += (_, _) =>
        {
            try
            {
                var frame = Selected(); using var raw = Cv2.ImRead(Path.Combine(directory, frame.File));
                var bitmap = raw.ToBitmapSource(); bitmap.Freeze(); _image.Source = bitmap;
                var label = _session.LoadLabel(frame) ?? new FrameLabel("Unknown", "Unknown", "", 0, 0, 0, 0);
                _state.Text = label.State; _outcome.SelectedItem = label.Outcome; _target.Text = label.Target;
                _bounds.Text = $"{label.X},{label.Y},{label.Width},{label.Height}";
                _status.Text = $"{frame.Seconds:F3}s | PC {_session.PcId} | Raw ROI {frame.Region}. Replay cannot prove alternative input outcomes.";
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
        if (_session.Frames.Count > 0) _frames.SelectedIndex = 0;
        else _status.Text = "No retained raw frames indexed in this session. Legacy video can be exported separately.";
    }
    private ReplayFrame Selected() => _frames.SelectedItem as ReplayFrame ?? throw new InvalidOperationException("Select a frame.");
    private FrameLabel Label()
    {
        var values = _bounds.Text.Split(',').Select(int.Parse).ToArray();
        if (values.Length != 4 || values.Any(v => v < 0)) throw new ArgumentException("Bounds require four non-negative integers.");
        return new(_state.Text, _outcome.SelectedItem?.ToString() ?? "Unknown", _target.Text, values[0], values[1], values[2], values[3]);
    }
}
