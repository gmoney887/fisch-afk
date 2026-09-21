using FischMacroCS.Core;
using FischMacroCS.Native;
using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Tests;

public class PreFlightExecutionTests
{
    [Fact]
    public async Task DeathScreenExplainsWhyReadinessFailedWithoutSendingInput()
    {
        var desktop = new Desktop { Width = 2254, Height = 1353 }; var input = new Input();
        using var death = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "death_wasted.png"));
        Assert.False(death.Empty());
        using var full = new Mat(desktop.Height, desktop.Width, MatType.CV_8UC3, Scalar.Black);
        using (var target = new Mat(full, new Rect(708, 406, death.Width, death.Height))) death.CopyTo(target);
        using var frames = new Frames(() => full.Clone());
        using var diagnostic = new PreFlightDiagnostic(new Settings(), capture: frames, desktop: desktop, hardware: input);
        var report = await diagnostic.RunDiagnosticAsync();
        using var snapshot = report.AnnotatedSnapshot;
        Assert.True(report.DeathDetected);
        Assert.False(report.OverallPass);
        Assert.False(report.ReconnectAvailable);
        Assert.False(report.ContinueAvailable);
        Assert.Contains("Character died", report.Summary);
        Assert.Empty(input.Edges);
    }

    private sealed class Desktop : GameDesktop
    {
        public IntPtr Window = (IntPtr)77;
        public bool Minimized, DenyFocus;
        public int Width = 1024, Height = 425, Finds, Activations;
        public uint Dpi = 96;
        public override IntPtr FindRobloxWindow() { Finds++; return Window; }
        public override bool IsIconic(IntPtr window) => Minimized;
        public override bool GetClientRect(IntPtr window, out Win32.RECT rect)
        { rect = new() { Right = Width, Bottom = Height }; return Window != IntPtr.Zero; }
        public override void ForceSetForegroundWindow(IntPtr window) => Activations++;
        public override IntPtr GetForegroundWindow() => DenyFocus ? IntPtr.Zero : Window;
        public override uint GetDpiForWindow(IntPtr window) => Dpi;
    }

    private sealed class Frames(Func<Mat> source) : IFrameSource
    {
        public int Captures;
        public bool Disposed;
        public Mat? CaptureClientRegion(IntPtr window, int x, int y, int width, int height)
        {
            Captures++;
            using var full = source();
            using var roi = new Mat(full, new Rect(x, y, width, height));
            return roi.Clone();
        }
        public void Dispose() => Disposed = true;
    }

    private sealed class Input : IInputSink
    {
        public List<uint> Edges = new();
        public Action? OnDown;
        public void keybd_event(byte key, byte scan, uint flags, int extra)
        { Assert.Equal((byte)'1', key); Edges.Add(flags); if (flags == 0) OnDown?.Invoke(); }
        public void SendHardwareMouseDown(int sx=-1,int sy=-1,int cx=-1,int cy=-1,IntPtr window=default) => throw new Exception("Unexpected mouse input");
        public void SendHardwareMouseUp(int sx=-1,int sy=-1,int cx=-1,int cy=-1,IntPtr window=default) => throw new Exception("Unexpected mouse input");
        public void SendHardwareMouseMove(int sx,int sy,int cx=-1,int cy=-1,IntPtr window=default) => throw new Exception("Unexpected mouse input");
        public void SendHardwareClick(int sx,int sy,int cx=-1,int cy=-1,IntPtr window=default) => throw new Exception("Unexpected mouse input");
        public void SendRelativeMove(int x,int y) => throw new Exception("Unexpected mouse input");
        public void SetCursorPos(int x,int y) => throw new Exception("Unexpected mouse input");
        public void SendKeyPress(char key) => throw new Exception("Must use guarded key edges");
        public void SendKeyString(string text,int delayMs=40) => throw new Exception("Unexpected typing");
        public void SelectAllAndClear() => throw new Exception("Unexpected typing");
        public void mouse_event(int flags,int x,int y,int data,int extra) => throw new Exception("Unexpected mouse input");
        public void ReleaseAll() => throw new Exception("Ownership belongs to GameplayInput");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReviewedRecoveryScreenPermitsRecoveryButNeverClaimsReadiness(bool continueScreen)
    {
        var desktop = new Desktop { Width = 2254, Height = 1353 }; var input = new Input();
        using var dialog = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", continueScreen ? "fisch_continue.png" : "disconnect_idle_278.png"));
        Assert.False(dialog.Empty());
        using var full = new Mat(desktop.Height, desktop.Width, MatType.CV_8UC3, Scalar.All(30));
        using (var target = new Mat(full, new Rect((full.Width-dialog.Width)/2, (full.Height-dialog.Height)/2, dialog.Width, dialog.Height)))
            dialog.CopyTo(target);
        using var frames = new Frames(() => full.Clone());
        using var diagnostic = new PreFlightDiagnostic(new Settings(), capture: frames, desktop: desktop, hardware: input);
        var report = await diagnostic.RunDiagnosticAsync();
        using var snapshot = report.AnnotatedSnapshot;
        Assert.Equal(!continueScreen, report.ReconnectAvailable);
        Assert.Equal(continueScreen, report.ContinueAvailable);
        Assert.False(report.OverallPass);
        Assert.Contains(continueScreen ? "Continue" : "Disconnected", report.Summary);
        Assert.Empty(input.Edges);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("minimized")]
    [InlineData("small")]
    public async Task UnavailableWindowFailsBeforeCaptureOrInput(string state)
    {
        var desktop = new Desktop(); var input = new Input();
        if (state == "missing") desktop.Window = IntPtr.Zero;
        if (state == "minimized") desktop.Minimized = true;
        if (state == "small") desktop.Width = 200;
        using var frames = new Frames(() => throw new Exception("Must not capture"));
        using var diagnostic = new PreFlightDiagnostic(new Settings(), capture: frames, desktop: desktop, hardware: input);
        var report = await diagnostic.RunDiagnosticAsync();
        Assert.False(report.OverallPass);
        Assert.False(report.ReconnectAvailable);
        Assert.Equal(new[] { "window", "capture", "hotbar", "tool_toggle", "coordinates" }, report.Steps.Select(s => s.Id));
        Assert.Equal(DiagnosticStatus.Fail, report.Steps[0].Status);
        Assert.All(report.Steps.Skip(1), s => Assert.Equal(DiagnosticStatus.Pending, s.Status));
        Assert.Equal(0, frames.Captures); Assert.Equal(0, desktop.Activations); Assert.Empty(input.Edges);
    }

    [Theory]
    [InlineData("window")]
    [InlineData("capture")]
    [InlineData("hotbar")]
    [InlineData("tool_toggle")]
    [InlineData("coordinates")]
    [InlineData("snapshot")]
    public async Task CancellationAtEachStepPreventsFurtherCaptureOrInput(string step)
    {
        var desktop = new Desktop(); var input = new Input();
        using var frames = new Frames(() => new Mat(425, 1024, MatType.CV_8UC3, Scalar.Black));
        using var diagnostic = new PreFlightDiagnostic(new Settings(), capture: frames, desktop: desktop, hardware: input);
        using var cancel = new CancellationTokenSource();
        int atCancel = -1;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => diagnostic.RunDiagnosticAsync(s =>
        {
            if (s.Id == step && s.Status == DiagnosticStatus.Running ||
                step == "snapshot" && s.Id == "coordinates" && s.Status == DiagnosticStatus.Pass)
            { atCancel = frames.Captures; cancel.Cancel(); }
        }, cancel.Token));
        Assert.True(atCancel >= 0);
        Assert.Equal(atCancel, frames.Captures); Assert.Empty(input.Edges);
        if (step == "window") Assert.Equal(0, desktop.Finds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingHotbarCannotPassReadinessOrSendProbe(bool black)
    {
        var desktop = new Desktop(); var input = new Input();
        using var frames = new Frames(() => new Mat(425, 1024, MatType.CV_8UC3, black ? Scalar.Black : Scalar.All(30)));
        using var diagnostic = new PreFlightDiagnostic(new Settings(), capture: frames, desktop: desktop, hardware: input);
        var report = await diagnostic.RunDiagnosticAsync();
        using var snapshot = report.AnnotatedSnapshot;
        Assert.False(report.OverallPass);
        Assert.Equal(DiagnosticStatus.Fail, report.Steps.Single(s => s.Id == "hotbar").Status);
        Assert.Equal(DiagnosticStatus.Fail, report.Steps.Single(s => s.Id == "tool_toggle").Status);
        if (black) Assert.Equal(DiagnosticStatus.Fail, report.Steps.Single(s => s.Id == "capture").Status);
        Assert.Empty(input.Edges);
    }

    [Theory]
    [InlineData("focus")]
    [InlineData("resize")]
    [InlineData("dpi")]
    public async Task ContextChangeBeforeProbePreventsInput(string change)
    {
        var desktop = new Desktop(); var input = new Input();
        using var source = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "user_screenshot.png"));
        Assert.False(source.Empty());
        using var frames = new Frames(() => source.Clone());
        using var diagnostic = new PreFlightDiagnostic(new Settings(), capture: frames, desktop: desktop, hardware: input);
        bool changed = false;
        await Assert.ThrowsAsync<GameplayInterruptedException>(() => diagnostic.RunDiagnosticAsync(s =>
        {
            if (s.Id != "tool_toggle" || s.Status != DiagnosticStatus.Running) return;
            changed = true;
            if (change == "focus") desktop.DenyFocus = true;
            if (change == "resize") desktop.Width++;
            if (change == "dpi") desktop.Dpi = 144;
        }));
        Assert.True(changed); Assert.Empty(input.Edges);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProbeVerifiesEquipmentAndReleasesKeyOnCancellation(bool cancelOnPress)
    {
        var desktop = new Desktop(); var input = new Input();
        using var source = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "user_screenshot.png"));
        Assert.False(source.Empty());
        using var equipped = source.Clone();
        Cv2.Rectangle(equipped, new Rect(425,398,13,15), new Scalar(240,160,20), -1);
        using var cancel = new CancellationTokenSource();
        input.OnDown = () => { if (cancelOnPress) cancel.Cancel(); };
        using var frames = new Frames(() => input.Edges.Count == 0 ? source.Clone() : equipped.Clone());
        using var diagnostic = new PreFlightDiagnostic(new Settings(), capture: frames, desktop: desktop, hardware: input);
        if (cancelOnPress)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => diagnostic.RunDiagnosticAsync(ct: cancel.Token));
        else
        {
            var report = await diagnostic.RunDiagnosticAsync();
            using var snapshot = report.AnnotatedSnapshot;
            Assert.True(report.OverallPass, report.Summary);
            Assert.Equal(DiagnosticStatus.Pass, report.Steps.Single(s => s.Id == "tool_toggle").Status);
            Assert.NotNull(snapshot);
        }
        Assert.Equal(new uint[] { 0, Win32.KEYEVENTF_KEYUP }, input.Edges);
        diagnostic.Dispose();
        Assert.False(frames.Disposed); // Caller-owned sources remain reusable.
    }
}
