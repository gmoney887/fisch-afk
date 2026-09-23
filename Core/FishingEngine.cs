using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using FischMacroCS.Capture;
using FischMacroCS.Native;
using FischMacroCS.Vision;

namespace FischMacroCS.Core;

public enum MacroState
{
    Stopped,
    Casting,
    Luring,
    Reeling,
    PostCatch
}

public class TelemetryData : IDisposable
{
    public MacroState State { get; set; }
    public string Action { get; set; } = "";
    public int BarLeft { get; set; }
    public int BarRight { get; set; }
    public double BarCenter { get; set; }
    public int BarWidth { get; set; }
    public int FishX { get; set; }
    public double Error { get; set; }
    public double BarVelocity { get; set; }
    public double FishVelocity { get; set; }
    public double RodPullAccel { get; set; }
    public double LoopLatencyMs { get; set; }
    public double VisionLatencyMs { get; set; }
    public bool IsMouseDown { get; set; }
    public Mat? AnnotatedFrame { get; set; }

    // Session Analytics
    public int TotalCatches { get; set; }
    public int TotalFails { get; set; }
    public int CurrentStreak { get; set; }
    public double SessionUptimeSeconds { get; set; }
    public double CatchesPerHour { get; set; }
    public double WinRate { get; set; }
    public int AntiAfkCount { get; set; }

    // Tool & Rod Vision Status
    public bool IsRodEquipped { get; set; } = false;
    public string RodStatusText { get; set; } = "ROD: STANDBY";

    // Autonomous Self-Healing Watchdog Status
    public int WatchdogRecoveryCount { get; set; } = 0;
    public bool IsWatchdogActive { get; set; } = true;
    public string WatchdogStatusText { get; set; } = "WATCHDOG: ARMED";

    public void Dispose()
    {
        AnnotatedFrame?.Dispose();
        AnnotatedFrame = null;
    }
}

public class FishingEngine : IDisposable
{
    public Settings Config { get; set; }
    public MacroState CurrentState { get; private set; } = MacroState.Stopped;
    public bool IsRunning => CurrentState != MacroState.Stopped;

    public event Action<TelemetryData>? OnTelemetry;

    private readonly IFrameSource _capture;
    private readonly IFrameSource _shakeCapture;
    private readonly VisionProcessor _vision = new();
    private readonly FlightRecorder _recorder;

    public FlightRecorder Recorder => _recorder;
    public double EstimatedRodPull => _estRodPullAccel;

    public string? LastCastReplicationDir { get; private set; }
    public string? LastAquariumReplicationDir { get; private set; }
    private volatile bool _isStopQueued;
    public bool IsStopQueued { get => _isStopQueued; set => _isStopQueued = value; }
    public bool TryQueueStopAfterCycle()
    {
        lock (_lifecycle)
        {
            if (!IsRunning || IsStopQueued) return false;
            IsStopQueued = true;
            return true;
        }
    }
    public string? LastStopSource { get; private set; }

    // Feature 1: Session Analytics & Catch Tracking
    public int TotalCatches { get; private set; } = 0;
    public int TotalFails { get; private set; } = 0;
    public int CurrentStreak { get; private set; } = 0;
    public int WatchdogRecoveryCount { get; private set; } = 0;
    private readonly Stopwatch _sessionStopwatch = new();
    private readonly object _statistics = new();
    // Worker-owned cumulative counters are never reset by the display's Reset button.
    private int _lifetimeCatches, _lifetimeFails, _lifetimeUnknown, _lifetimeRecoveries;
    private bool _lastKnownRodEquipped = false;
    private bool _lastHotbarGeometryConfirmed;
    private string _lastKnownRodStatus = "ROD: STANDBY";
    private bool _catchBannerSeen = false;
    private int _catchBannerFrames;
    private int _readyHotbarFrames;

    // Feature 6: Autonomous Self-Healing Watchdog
    private long _lastProgressTicks = 0;
    private readonly object _recoveryLock = new();
    private bool _isRecovering = false;
    private readonly AutomationCoordinator _coordinator = new();
    private readonly object _lifecycle = new();
    private bool _disposed;
    private readonly IClock _clock;
    private readonly GameDesktop _desktop;
    private readonly string _workflowTemplateDirectory;
    public Task Completion => _workerTask ?? Task.CompletedTask;
    private readonly IInputSink _input;
    private readonly RecoveryBudget _recoveryBudget = new();
    private readonly FishingViewGuard _fishingView = new();
    private long _lastViewCheck;
    private bool _viewChecked;
    private CatchOutcomeTracker _catchOutcome = new();
    private CancellationToken _operationCancellation;
    private CancellationTokenSource? _manualCancellation;
    private CancellationTokenSource? _tailCancellation;
    private IntPtr _activeWindow;
    private Win32.RECT _activeGeometry;
    private uint _activeDpi;
    private int _recordingStartCatches, _recordingStartFails, _recordingStartUnknown;
    private long _recordingStarted;
    private int _recordingStartRecoveries;
    public string? PauseReason { get; private set; }
    public int UnknownCatches { get; private set; }
    public ActionOutcome LastAquariumOutcome { get; private set; }
    public string? LastAquariumEvidence { get; private set; }
    public ActionOutcome LastCrateOutcome { get; private set; }
    public string? LastCrateEvidence { get; private set; }

    private void ValidateGameplay()
    {
        _coordinator.ThrowIfCancelled();
        _operationCancellation.ThrowIfCancellationRequested();
        if (!_coordinator.IsOwner) throw new InvalidOperationException("Input must run on the coordinator.");
        if (_activeWindow == IntPtr.Zero || _desktop.IsIconic(_activeWindow) ||
            _desktop.GetForegroundWindow() != _activeWindow ||
            !_desktop.GetClientRect(_activeWindow, out var geometry) || geometry.Width <= 0 || geometry.Height <= 0)
            throw new GameplayInterruptedException("Roblox must be visible and focused with a valid capture.");
        if (geometry.Width != _activeGeometry.Width || geometry.Height != _activeGeometry.Height ||
            _desktop.GetDpiForWindow(_activeWindow) != _activeDpi)
            throw new GameplayInterruptedException("Window geometry changed; reacquiring the viewport is required.");
    }

    private void BeginGameplay()
    {
        _deathContextRecorded = false;
        _coordinator.ThrowIfCancelled();
        _operationCancellation.ThrowIfCancellationRequested();
        _activeWindow = _desktop.FindRobloxWindow();
        if (_activeWindow != IntPtr.Zero) _desktop.ForceSetForegroundWindow(_activeWindow);
        _desktop.GetClientRect(_activeWindow, out _activeGeometry);
        _activeDpi = _desktop.GetDpiForWindow(_activeWindow);
        ValidateGameplay();
        _recordingStartCatches = _lifetimeCatches; _recordingStartFails = _lifetimeFails; _recordingStartUnknown = _lifetimeUnknown;
        _recordingStartRecoveries = _lifetimeRecoveries;
        _recordingStarted = _clock.Timestamp;
        if (Config.EnableRecording) _recorder.StartSession(_activeGeometry.Width, _activeGeometry.Height,
            new { Settings = Config, TotalCatches, TotalFails, UnknownCatches, SessionUptimeSeconds, Dpi = _activeDpi,
                Adaptation = Config.EnableAdaptiveRodDynamics ? BoundedRodEstimator.Load(AppDataPaths.FilePath("rod-profile.json"),
                    Config.RodProfile, _activeGeometry.Width, _activeGeometry.Height) : null });
    }

    private void WaitForFishingRetry(string reason, int minimumDelayMs = 1000)
    {
        SessionLogger.Instance.Log("WATCHDOG", "Waiting to retry fishing: " + reason);
        _recorder.RecordEvent("fishing-retry", new { Reason = reason });
        _recoveryContextRecorded = false;
        FishingRetryWait.Wait(_clock, () =>
        {
            _coordinator.ThrowIfCancelled();
            var window = _desktop.FindRobloxWindow();
            return _afkRecovery.Poll(
                () => window != IntPtr.Zero && !_desktop.IsIconic(window) && _desktop.GetForegroundWindow() == window,
                () => { if (window != IntPtr.Zero) { _desktop.ShowWindowAsync(window, Win32.SW_RESTORE); _desktop.ForceSetForegroundWindow(window); } },
                () =>
                {
                    if (!_desktop.GetClientRect(window, out var geometry) || geometry.Width < 300 || geometry.Height < 200) return RecoveryView.Unknown;
                    _activeWindow = window; _activeGeometry = geometry; _activeDpi = _desktop.GetDpiForWindow(window);
                    return ObserveRecoveryView();
                }, TryReconnect, _operationCancellation, TryContinueLoading);
        }, () => { _input.ReleaseAll(); _isMouseDown = false; }, () =>
        {
            _coordinator.ThrowIfCancelled();
            if (IsStopQueued) { Stop("Queued stop during recovery"); _operationCancellation.ThrowIfCancellationRequested(); }
            _input.ReleaseAll(); // Retry any up edges the OS previously rejected.
            var telemetry = new TelemetryData { State = CurrentState, Action = _afkRecovery.Status + ": " + reason };
            PopulateTelemetryStats(telemetry);
            telemetry.WatchdogStatusText = "WATCHDOG: WAITING TO RETRY";
            if (OnTelemetry != null) OnTelemetry(telemetry); else telemetry.Dispose();
        }, _operationCancellation, minimumDelayMs);
        // A visible window alone is not progress. Only verified gameplay exits this wait.
        _lastProgressTicks = _clock.Timestamp;
        if (!_recoveryResumedReel)
            Transition(MacroState.Casting, "Fishing context reacquired; checking for an active reel before casting");
    }

    private void Delay(int milliseconds)
    {
        int remaining = Math.Max(0, milliseconds);
        do
        {
            ValidateGameplay();
            int slice = Math.Min(20, remaining);
            _clock.Delay(slice, _operationCancellation);
            remaining -= slice;
        } while (remaining > 0);
        ValidateGameplay();
    }

    private T RunManual<T>(Func<T> action, T interrupted, CancellationToken cancellation = default)
    {
        if (IsRunning) return action();
        using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        lock (_lifecycle)
        {
            if (_disposed) return interrupted;
            _manualCancellation = source;
        }
        _operationCancellation = source.Token;
        PauseReason = null;
        try { BeginGameplay(); return action(); }
        catch (OperationCanceledException) { return interrupted; }
        catch (GameplayInterruptedException ex) { PauseReason = ex.Message; return interrupted; }
        catch (Exception ex) { PauseReason = "Workflow interrupted: " + ex.Message; SessionLogger.Instance.LogError("Manual workflow", ex); return interrupted; }
        finally
        {
            _input.ReleaseAll(); _isMouseDown = false;
            _recorder.StopSession(PauseReason ?? "Manual workflow completed");
            lock (_lifecycle) _manualCancellation = null;
        }
    }

    private T RunQueued<T>(Func<T> action, T interrupted, CancellationToken cancellation = default)
    {
        try { return _coordinator.Enqueue(() => RunManual(action, interrupted, cancellation)).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { return interrupted; }
        catch (ObjectDisposedException) { return interrupted; }
        catch (InvalidOperationException ex) { PauseReason = ex.Message; return interrupted; }
    }

    private void Pause(string reason)
    {
        PauseReason = reason;
        SessionLogger.Instance.Log("PAUSED", reason);
        Stop("Automation interruption");
    }

    public double SessionUptimeSeconds => _sessionStopwatch.Elapsed.TotalSeconds;
    public double CatchesPerHour => (SessionUptimeSeconds > 5) ? (TotalCatches * 3600.0 / SessionUptimeSeconds) : 0.0;
    public double WinRate => (TotalCatches + TotalFails > 0) ? (TotalCatches * 100.0 / (TotalCatches + TotalFails)) : 100.0;

    public void ResetStats()
    {
        lock (_statistics)
        {
        _recorder.RecordEvent("statistics-reset", new { TotalCatches, TotalFails, UnknownCatches, SessionUptimeSeconds });
        TotalCatches = 0;
        TotalFails = 0;
        UnknownCatches = 0;
        CurrentStreak = 0;
        WatchdogRecoveryCount = 0;
        bool timing = _sessionStopwatch.IsRunning;
        _sessionStopwatch.Reset();
        if (timing) _sessionStopwatch.Start();
        }
    }

    // Periodic input heartbeat; OS delivery does not prove game acceptance.
    private int _antiAfkCount = 0;
    private readonly AntiIdleHeartbeat _heartbeat;
    private readonly AfkRecovery _afkRecovery;
    private bool _recoveryResumedReel;
    private bool _recoveryContextRecorded;
    private bool _deathContextRecorded;

    private RecoveryView ObserveRecoveryView()
    {
        _recoveryResumedReel = false;
        CheckAntiAfkHeartbeat();
        int h = _activeGeometry.Height, w = _activeGeometry.Width;
        int roiW = Math.Min(w, (int)(h * .62)), roiH = Math.Min(h, (int)(h * .40));
        int x = (w-roiW)/2, y = (h-roiH)/2;
        using var frame = _shakeCapture.CaptureClientRegion(_activeWindow, x, y, roiW, roiH);
        if (frame == null || frame.Empty()) return RecoveryView.Unknown;
        if (!_recoveryContextRecorded)
        {
            _recorder.RecordEvent("recovery-context", new { Status = "Gameplay unavailable", Outcome = ActionOutcome.Unknown });
            _recoveryContextRecorded = true;
        }
        // Retain central context while waiting, including update/disconnect screens.
        if (DeathScreenDetector.IsDeathScreen(frame, h))
        {
            if (!_deathContextRecorded)
                _recorder.RecordEvent("death-detected", new { Status = "Waiting for gameplay; fishing location must be restored" });
            _deathContextRecorded = true;
            return RecoveryView.Death;
        }
        _deathContextRecorded = false;
        if (DisconnectDetector.TryFindReconnect(frame, h, out _)) return RecoveryView.Reconnect;
        if (ContinueScreenDetector.IsContinueScreen(frame, h)) return RecoveryView.Continue;
        if (TryResumeVisibleReel()) { _recoveryResumedReel = true; return RecoveryView.Gameplay; }
        IsRodEquipped(_activeWindow, w, h);
        return _lastHotbarGeometryConfirmed ? RecoveryView.Gameplay : RecoveryView.Loading;
    }

    private void TryReconnect()
    {
        ValidateGameplay();
        int h = _activeGeometry.Height, w = _activeGeometry.Width;
        int rw = Math.Min(w, (int)(h * .62)), rh = Math.Min(h, (int)(h * .40));
        int x = (w - rw) / 2, y = (h - rh) / 2;
        // Never click coordinates cached by the preceding observation: the dialog
        // may already have closed, changed, or started loading.
        using var frame = _shakeCapture.CaptureClientRegion(_activeWindow, x, y, rw, rh);
        if (frame == null || !DisconnectDetector.TryFindReconnect(frame, h, out var target)) return;
        var snapped = _vision.DynamicUISnapWithStatus(frame, target.X, target.Y,
            VisionProcessor.UIColorType.WhiteButton, Math.Max(12, (int)(h * .035)));
        if (!snapped.Found || Math.Abs(snapped.Pt.X - target.X) > h * .015 || Math.Abs(snapped.Pt.Y - target.Y) > h * .015) return;
        ValidateGameplay();
        var point = new Point(x + snapped.Pt.X, y + snapped.Pt.Y);
        var screen = new Win32.POINT { X = point.X, Y = point.Y };
        if (!_desktop.ClientToScreen(_activeWindow, ref screen)) throw new GameplayInterruptedException("Reconnect coordinate conversion failed.");
        _recorder.RecordEvent("reconnect-attempt", new { X = point.X, Y = point.Y });
        _input.SendHardwareClick(screen.X, screen.Y, point.X, point.Y, _activeWindow);
        // A delivered click is not recovery: AfkRecovery keeps waiting for gameplay.
    }

    private void TryContinueLoading()
    {
        ValidateGameplay();
        int h = _activeGeometry.Height, w = _activeGeometry.Width;
        int rw = Math.Min(w, (int)(h * .62)), rh = Math.Min(h, (int)(h * .40));
        int x = (w-rw)/2, y = (h-rh)/2;
        using var frame = _shakeCapture.CaptureClientRegion(_activeWindow, x, y, rw, rh);
        if (frame == null || !ContinueScreenDetector.TryFindPrompt(frame, h, out var bounds)) return;
        using var prompt = new Mat(frame, bounds);
        var snapped = _vision.DynamicUISnapWithStatus(prompt, prompt.Width/2, prompt.Height/2,
            VisionProcessor.UIColorType.WhiteText, Math.Max(12, (int)(h * .02)));
        if (!snapped.Found || !new Rect(0,0,prompt.Width,prompt.Height).Contains(snapped.Pt)) return;
        ValidateGameplay();
        var point = new Point(x + bounds.X + snapped.Pt.X, y + bounds.Y + snapped.Pt.Y);
        var screen = new Win32.POINT { X = point.X, Y = point.Y };
        if (!_desktop.ClientToScreen(_activeWindow, ref screen)) throw new GameplayInterruptedException("Continue coordinate conversion failed.");
        _recorder.RecordEvent("continue-loading", new { X = point.X, Y = point.Y });
        // Live probes accepted clicking this prompt but ignored Enter and Space.
        // The next recovery observation must still verify gameplay before casting.
        _input.SendHardwareClick(screen.X, screen.Y, point.X, point.Y, _activeWindow);
    }

    private void SendShakeClick(int screenX, int screenY, int clientX, int clientY, IntPtr window)
    {
        // Preserve the native helper's relative hover motion through the guarded sink.
        // Absolute moves alone can leave a stationary shake target unresponsive.
        _input.SendHardwareMouseMove(screenX, screenY, clientX, clientY, window);
        _input.SendRelativeMove(3, 2); Delay(10);
        _input.SendRelativeMove(-3, -2); Delay(10);
        _input.SendRelativeMove(-2, 2); Delay(10);
        _input.SendRelativeMove(2, -2); Delay(15);
        _input.SendHardwareMouseMove(screenX, screenY, clientX, clientY, window);
        _input.SendHardwareMouseDown(screenX, screenY, clientX, clientY, window);
        try
        {
            Delay(20);
            _input.SendRelativeMove(1, 0); Delay(10);
            _input.SendRelativeMove(-1, 0); Delay(15);
        }
        finally { _input.SendHardwareMouseUp(); }
        _input.SendHardwareMouseMove(screenX, screenY, clientX, clientY, window);
    }

    // Feature 3: Humanized Timing Jitter (Anti-Macro Detection)
    private static readonly Random _jitterRng = new();

    public int GetJitteredMs(int baseMs, int jitterRangeMs)
    {
        if (!Config.JitterEnabled || jitterRangeMs <= 0) return baseMs;
        int jitter = _jitterRng.Next(-jitterRangeMs, jitterRangeMs + 1);
        return Math.Max(10, baseMs + jitter);
    }

    // Feature 4: Rod Physics Tuning Presets
    private double _baseRodPullAccel = 550.0;
    private double _gravityFallAccel = 410.0;
    private double _deadzoneFactor = 0.045;
    private double _edgeMarginFactor = 0.16;

    public void ApplyRodProfile(string profileName)
    {
        Config.RodProfile = profileName;
        switch (profileName)
        {
            case "Destiny / Mythic":
                _baseRodPullAccel = 720.0;
                _gravityFallAccel = 440.0;
                _deadzoneFactor = 0.035;
                _edgeMarginFactor = 0.14;
                break;
            case "Rod of the Depths":
                _baseRodPullAccel = 620.0;
                _gravityFallAccel = 390.0;
                _deadzoneFactor = 0.040;
                _edgeMarginFactor = 0.15;
                break;
            case "Heaven's / Sunken":
                _baseRodPullAccel = 680.0;
                _gravityFallAccel = 420.0;
                _deadzoneFactor = 0.030;
                _edgeMarginFactor = 0.13;
                break;
            case "Magma / Steady":
                _baseRodPullAccel = 480.0;
                _gravityFallAccel = 360.0;
                _deadzoneFactor = 0.050;
                _edgeMarginFactor = 0.18;
                break;
            case "Rapid / Carbon":
                _baseRodPullAccel = 580.0;
                _gravityFallAccel = 420.0;
                _deadzoneFactor = 0.040;
                _edgeMarginFactor = 0.15;
                break;
            case "Standard":
            default:
                _baseRodPullAccel = 550.0;
                _gravityFallAccel = 410.0;
                _deadzoneFactor = 0.045;
                _edgeMarginFactor = 0.16;
                break;
        }
        _estRodPullAccel = _baseRodPullAccel;
        _rodEstimator = null;
        _adaptationTrackWidth = 0;
    }

    private void PopulateTelemetryStats(TelemetryData telem)
    {
        telem.TotalCatches = TotalCatches;
        telem.TotalFails = TotalFails;
        telem.CurrentStreak = CurrentStreak;
        telem.SessionUptimeSeconds = SessionUptimeSeconds;
        telem.CatchesPerHour = CatchesPerHour;
        telem.WinRate = WinRate;
        telem.AntiAfkCount = _antiAfkCount;
        telem.WatchdogRecoveryCount = WatchdogRecoveryCount;
        telem.IsWatchdogActive = Config.EnableWatchdogRecovery && IsRunning;
        telem.WatchdogStatusText = _isRecovering ? "WATCHDOG: HEALING" : (Config.EnableWatchdogRecovery ? "WATCHDOG: ARMED" : "WATCHDOG: OFF");

        telem.IsRodEquipped = _lastKnownRodEquipped;
        telem.RodStatusText = _lastKnownRodStatus;
    }

    private void CheckAntiAfkHeartbeat()
    {
        if (_heartbeat.TrySend(Config.EnableAntiAfk, () =>
        {
            ValidateGameplay();
            // F15 has no default movement/menu binding; unlike a right-drag it cannot turn the camera.
            _input.keybd_event(0x7E, 0, 0, 0);
            Delay(50);
        }, () => _input.keybd_event(0x7E, 0, Win32.KEYEVENTF_KEYUP, 0), _operationCancellation, Config.AntiAfkIntervalMinutes))
        {
            _antiAfkCount = _heartbeat.Completed;
            SessionLogger.Instance.Log("ANTI-AFK", "SendInput heartbeat completed; game acceptance requires live validation.");
            _recorder.RecordEvent("heartbeat", new { Completed = _antiAfkCount });
        }
    }

    // Feature 5: Personal Aquarium Auto-Claim Rewards (Hourly)
    private readonly AquariumSchedule _aquariumSchedule;
    private bool _isAquariumClaimPending = false;
    private bool _skipAquariumThisSession;
    private bool _aquariumCanRetryWithoutRecovery;
    private bool _skipCratesThisSession;

    public void CheckAquariumClaimHeartbeat()
    {
        if (!Config.EnableAutoClaimAquarium || _skipAquariumThisSession) return;
        DateTime lastCheck = Config.LastAquariumCheckUtc > Config.LastAquariumClaimUtc
            ? Config.LastAquariumCheckUtc : Config.LastAquariumClaimUtc;
        if (_aquariumSchedule.IsDue(lastCheck, Config.AquariumClaimIntervalMinutes, DateTime.UtcNow))
        {
            _isAquariumClaimPending = true;
        }
    }

    public void TriggerImmediateAquariumClaim()
    {
        _isAquariumClaimPending = true;
    }

    private WorkflowResult RunRewardWorkflow(bool aquarium, Action<string>? progress = null)
    {
        ValidateGameplay();
        int w = _activeGeometry.Width, h = _activeGeometry.Height;
        WorkflowTarget Target(string name, double x, double y, double radius = .1) => new(name, x, y, radius);
        void Click(Point point)
        {
            ValidateGameplay();
            var screen = new Win32.POINT { X = point.X, Y = point.Y };
            if (!_desktop.ClientToScreen(_activeWindow, ref screen)) throw new GameplayInterruptedException("Coordinate conversion failed.");
            _input.SendHardwareClick(screen.X, screen.Y, point.X, point.Y, _activeWindow);
        }
        using var workflowVision = new TemplateWorkflowVision(_workflowTemplateDirectory);
        var workflow = new VerifiedWorkflow(_clock, workflowVision,
            () => _capture.CaptureClientRegion(_activeWindow, 0, 0, w, h), Delay,
            message => { SessionLogger.Instance.Log("WORKFLOW", message); progress?.Invoke(message); });
        if (aquarium)
        {
            return AquariumWorkflow.Run(_clock, workflowVision,
                () => _capture.CaptureClientRegion(_activeWindow, 0, 0, w, h), Delay, Click, _operationCancellation,
                message => { SessionLogger.Instance.Log("AQUARIUM", message); progress?.Invoke(message); });
        }
        var search = workflow.Run([
            new("Open equipment", Target("equipment-button", 0, .9, .25), Target("equipment-search", .0498, .298), Click),
            new("Search crates", Target("equipment-search", .0498, .298), Target("crate-item", -.1458, .435), point =>
                { Click(point); _input.SelectAllAndClear(); _input.SendKeyString("crate"); }),
        ], _operationCancellation);
        if (search.Outcome != ActionOutcome.ConfirmedSuccess)
        {
            if (search.Evidence != "Search crates: expected result not detected") return search;
            // Empty inventory requires its own positive visual evidence, never a missing dialog.
            using var emptyFrame = _capture.CaptureClientRegion(_activeWindow, 0, 0, w, h);
            using var emptyVision = new TemplateWorkflowVision(_workflowTemplateDirectory);
            if (emptyFrame == null || !emptyVision.Find(emptyFrame, Target("equipment-empty", 0, .5, .4)).Found) return search;
            var closed = workflow.Run([new("Close empty equipment", Target("equipment-close", .3, .3, .3),
                Target("equipment-search", .0498, .298), Click, ExpectedPresent: false)], _operationCancellation);
            return closed.Outcome == ActionOutcome.ConfirmedSuccess
                ? new(ActionOutcome.ConfirmedSuccess, "Empty inventory and equipment closure visually confirmed") : closed;
        }
        return workflow.Run([
            new("Select crate", Target("crate-item", -.1458, .435), Target("crate-quantity", 0, .52), Click),
            new("Select all", Target("crate-max", 0, .52, .3), Target("crate-all-selected", 0, .52, .3), Click),
            new("Open crate", Target("crate-confirm", -.1173, .571), Target("crate-reward", 0, .5, .4), Click, 8000),
            new("Dismiss reward", Target("crate-reward-close", 0, .5, .4), Target("crate-reward", 0, .5, .4), Click, ExpectedPresent: false),
            new("Close equipment", Target("equipment-close", .3, .3, .3), Target("equipment-search", .0498, .298), Click, ExpectedPresent: false)
        ], _operationCancellation);
    }

    public bool ExecuteAquariumClaim(bool recordReplication = true)
    {
        if (!_coordinator.IsOwner) return RunQueued(() => ExecuteAquariumClaim(recordReplication), false);
        _isAquariumClaimPending = false;
        LastAquariumOutcome = ActionOutcome.Unknown;
        LastAquariumEvidence = null;
        _aquariumCanRetryWithoutRecovery = false;
        var result = RunRewardWorkflow(true);
        _recorder.RecordEvent("aquarium-outcome", result);
        LastAquariumOutcome = result.Outcome;
        LastAquariumEvidence = result.Evidence;
        SessionLogger.Instance.Log("AQUARIUM", result.Evidence);
        if (result.Outcome != ActionOutcome.ConfirmedSuccess)
        {
            _aquariumCanRetryWithoutRecovery = result.RetryableWithoutRecovery;
            if (_aquariumCanRetryWithoutRecovery) _aquariumSchedule.Defer();
            return false;
        }
        _aquariumSchedule.Checked();
        Config.LastAquariumCheckUtc = DateTime.UtcNow;
        if (result.RewardClaimed) Config.LastAquariumClaimUtc = Config.LastAquariumCheckUtc;
        Config.Save();
        return true;
    }

    public bool ExecuteAutoOpenCrates(int maxCrateTypes = 25, Action<string>? onProgress = null, CancellationToken ct = default)
    {
        if (!_coordinator.IsOwner)
        {
            LastCrateEvidence = null;
            LastCrateOutcome = ActionOutcome.Unknown;
            return RunQueued(() => ExecuteAutoOpenCrates(maxCrateTypes, onProgress, ct), false, ct);
        }
        LastCrateOutcome = ActionOutcome.Unknown;
        // An absent dialog never proves an empty inventory. Each type requires its own reward evidence.
        int limit = maxCrateTypes > 0 ? Math.Min(maxCrateTypes, 25) : 25;
        for (int i = 0; i < limit; i++)
        {
            ct.ThrowIfCancellationRequested();
            var result = RunRewardWorkflow(false, onProgress);
            LastCrateEvidence = result.Evidence;
            SessionLogger.Instance.Log("CRATES", result.Evidence);
            onProgress?.Invoke(result.Evidence);
            if (result.Outcome != ActionOutcome.ConfirmedSuccess)
            {
                LastCrateOutcome = result.Outcome;
                _recorder.RecordEvent("crate-outcome", result);
                return false;
            }
            EnsureRodEquipped(_activeWindow, _activeGeometry.Width, _activeGeometry.Height);
            LastCrateOutcome = result.Outcome;
            _recorder.RecordEvent("crate-outcome", result);
            if (result.Evidence.StartsWith("Empty inventory", StringComparison.Ordinal)) return true;
        }
        if (maxCrateTypes == 0 || maxCrateTypes > limit)
        {
            LastCrateOutcome = ActionOutcome.Unknown;
            LastCrateEvidence = "Per-command limit reached; empty inventory not confirmed";
            _recorder.RecordEvent("crate-outcome", new { Outcome = "Unknown", Evidence = "Per-command limit reached; empty inventory not confirmed" });
            return false;
        }
        return true;
    }

    private bool IsBagOpen(IntPtr window, int width, int height)
    {
        using var frame = _capture.CaptureClientRegion(window, 0, 0, width, height);
        using var vision = new TemplateWorkflowVision(_workflowTemplateDirectory);
        return frame != null && !frame.Empty() && vision.Find(frame, new WorkflowTarget("equipment-search", .0498, .298, .1)).Found;
    }

    private int _catchesSinceLastCrateOpen = 0;
    private CancellationTokenSource? _cts;
    private Task? _workerTask;

    private bool _isMouseDown = false;
    private long _stateStartTime = 0;
    private long _lastBarSeenTime = 0;
    private long _lastFishSeenTime = 0;
    private int _luringConfirmCount = 0;

    private double _prevBarCenter = 0;
    private double _barVelocity = 0;
    private double _prevFishX = 0;
    private double _fishVelocity = 0;
    private long _lastTickTime = 0;

    private bool _pulseState = false;
    private long _lastPulseTime = 0;
    private int _pulseDuration = 25;
    private double _dutyAccumulator = 0.0;
    private double _estRodPullAccel = 550.0;
    private double _prevBarVelocity = 0;
    private BoundedRodEstimator? _rodEstimator;
    private BoundedRodEstimator? _releaseEstimator;
    private int _adaptationTrackWidth;
    private bool _adaptiveEnabled;

    private long _lastDebugFrameTick = 0;
    private long _lastShakeClickTime = 0;
    private int _shakeMemoryX = 0;
    private int _shakeMemoryY = 0;
    private int _shakeRepeatCounter = 0;

    public FishingEngine(Settings settings, IFrameSource? frameSource = null, IFrameSource? contextSource = null, IInputSink? input = null, IClock? clock = null, GameDesktop? desktop = null, IInputSink? hardware = null, string? workflowTemplateDirectory = null, string? recordingDirectory = null)
    {
        _recorder = new FlightRecorder(recordingDirectory);
        _desktop = desktop ?? new GameDesktop();
        _workflowTemplateDirectory = workflowTemplateDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Workflows");
        _capture = new RecordingFrameSource(frameSource ?? new ScreenCapture(), _recorder, () => new Rect(0, 0, _activeGeometry.Width, _activeGeometry.Height), clock, ValidateGameplay);
        _shakeCapture = new RecordingFrameSource(contextSource ?? new ScreenCapture(), _recorder, () => new Rect(0, 0, _activeGeometry.Width, _activeGeometry.Height), clock, ValidateGameplay);
        _clock = clock ?? new MonotonicClock();
        _aquariumSchedule = new AquariumSchedule(_clock);
        _heartbeat = new AntiIdleHeartbeat(_clock);
        _afkRecovery = new AfkRecovery(_clock);
        _input = input ?? new GameplayInput(ValidateGameplay, Delay, name => _recorder.RecordEvent("input",
            new { Action = name, State = CurrentState.ToString(), Recovering = _isRecovering, Source = "Macro" }), hardware);
        Config = settings;
        _recorder.MaxRecordingsToKeep = Config.MaxRecordingsToKeep;
        ApplyRodProfile(Config.RodProfile);
    }

    public void Start(bool observeOnly = false)
    {
        lock (_lifecycle)
        {
        if (_disposed) return;
        if (IsRunning || _workerTask is { IsCompleted: false } || _manualCancellation != null) return;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsStopQueued = false;
        LastStopSource = null;
        var cancellation = _cts.Token;
        PauseReason = null;
        _recoveryBudget.ConfirmProgress();
        _fishingView.Reset();
        _viewChecked = false;
        _catchBannerFrames = 0; _catchBannerSeen = false;
        _skipAquariumThisSession = _skipCratesThisSession = false;
        _isRecovering = false;
        _lastProgressTicks = _clock.Timestamp;
        CurrentState = MacroState.Casting;
        _stateStartTime = _clock.Timestamp;
        _heartbeat.Reset();
        _afkRecovery.Reset();
        _antiAfkCount = 0;
        lock (_statistics) _sessionStopwatch.Start();
        _workerTask = _coordinator.Enqueue(() =>
        {
            _operationCancellation = cancellation;
            _desktop.timeBeginPeriod(1);
            try
            {
                while (true)
                {
                    try { BeginGameplay(); break; }
                    catch (GameplayInterruptedException ex) when (!observeOnly)
                    { WaitForFishingRetry(ex.Message); }
                }
                if (observeOnly) ObserveGameplay(cancellation);
                else WorkerLoop(cancellation);
            }
            catch (GameplayInterruptedException ex) { Pause(ex.Message); }
            catch (FishingSafetyException ex) { Pause(ex.Message); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Pause("Automation stopped: " + ex.Message); }
            finally
            {
                _input.ReleaseAll(); _isMouseDown = false;
                if (PauseReason != null) CaptureDiagnosticTail();
                double duration = Math.Max(0, _clock.ElapsedMilliseconds(_recordingStarted) / 1000);
                _recorder.StopSession(PauseReason ?? "Stopped", new { TotalCatches = _lifetimeCatches - _recordingStartCatches,
                    TotalFails = _lifetimeFails - _recordingStartFails, UnknownCatches = _lifetimeUnknown - _recordingStartUnknown,
                    SessionUptimeSeconds = duration, CatchesPerHour = duration > 0 ? (_lifetimeCatches - _recordingStartCatches) * 3600 / duration : 0,
                    WatchdogRecoveryCount = _lifetimeRecoveries - _recordingStartRecoveries, LastAquariumOutcome, LastCrateOutcome,
                    StopSource = LastStopSource ?? "Worker exited without a stop request" });
                _desktop.timeEndPeriod(1);
                lock (_statistics) _sessionStopwatch.Stop();
                CurrentState = MacroState.Stopped;
                // Refresh the recording indicator after asynchronous recorder shutdown.
                var finalTelemetry = new TelemetryData { State = MacroState.Stopped, Action = PauseReason ?? "Stopped" };
                PopulateTelemetryStats(finalTelemetry);
                if (OnTelemetry != null) OnTelemetry(finalTelemetry); else finalTelemetry.Dispose();
            }
            return true;
        });
        }
    }

    public void Stop(string source = "Requested")
    {
        lock (_lifecycle)
        {
            // Preserve the first cause: cleanup/disposal must not relabel a user stop.
            if (LastStopSource == null && (IsRunning || _workerTask is { IsCompleted: false } || _manualCancellation != null))
            {
                LastStopSource = source;
                _recorder.RecordEvent("stop-request", new { Source = source, State = CurrentState.ToString(), IsStopQueued, PauseReason });
            }
            IsStopQueued = false;
            _coordinator.CancelPending();
            _cts?.Cancel();
            _manualCancellation?.Cancel();
            _tailCancellation?.Cancel();
            _input.ReleaseAll();
            _isMouseDown = false;
            lock (_statistics) _sessionStopwatch.Stop();
            CurrentState = MacroState.Stopped;
        }
        var telem = new TelemetryData { State = MacroState.Stopped, Action = PauseReason ?? "Stopped" };
        PopulateTelemetryStats(telem);
        OnTelemetry?.Invoke(telem);
    }

    private void ObserveGameplay(CancellationToken cancellation)
    {
        // Shares coordinator/capture ownership but never sends gameplay input.
        if (!_recorder.IsRecording)
            _recorder.StartSession(_activeGeometry.Width, _activeGeometry.Height, new { Mode = "Passive", Dpi = _activeDpi });
        long marked = _clock.Timestamp;
        _recorder.RecordEvent("demonstration-outcome", new { Outcome = "Unknown", Evidence = "Manual demonstration" });
        while (!cancellation.IsCancellationRequested)
        {
            ValidateGameplay();
            using var frame = _capture.CaptureClientRegion(_activeWindow, 0, 0, _activeGeometry.Width, _activeGeometry.Height);
            if (_clock.ElapsedMilliseconds(marked) >= 4000)
            {
                _recorder.RecordEvent("demonstration-outcome", new { Outcome = "Unknown", Evidence = "Manual demonstration" });
                marked = _clock.Timestamp;
            }
            var telemetry = new TelemetryData { State = CurrentState, Action = "Passive observation — demonstrate gameplay; End stops recording", AnnotatedFrame = frame?.Clone() };
            PopulateTelemetryStats(telemetry);
            if (OnTelemetry != null) OnTelemetry(telemetry); else telemetry.Dispose();
            Delay(100);
        }
    }

    private void CaptureDiagnosticTail()
    {
        if (!_recorder.IsRecording) return;
        using var source = new CancellationTokenSource();
        lock (_lifecycle)
        {
            if (_disposed) return;
            _tailCancellation = source;
        }
        _recorder.RecordEvent("pause-outcome", new { Outcome = "Unknown", Reason = PauseReason });
        try
        {
            using var capture = new ScreenCapture();
            long start = _clock.Timestamp;
            while (_clock.ElapsedMilliseconds(start) < 5000 && !source.IsCancellationRequested)
            {
                // Do not record another application or keep obsolete geometry after focus/size loss.
                if (_activeWindow == IntPtr.Zero || _desktop.GetForegroundWindow() != _activeWindow || _desktop.IsIconic(_activeWindow) ||
                    !_desktop.GetClientRect(_activeWindow, out var geometry) || geometry.Width != _activeGeometry.Width ||
                    geometry.Height != _activeGeometry.Height || _desktop.GetDpiForWindow(_activeWindow) != _activeDpi)
                {
                    _recorder.RecordEvent("tail-unavailable", new { Reason = "Focus or geometry changed", CapturedMs = _clock.ElapsedMilliseconds(start) });
                    return;
                }
                using var frame = capture.CaptureClientRegion(_activeWindow, 0, 0, geometry.Width, geometry.Height);
                if (frame == null || frame.Empty()) return;
                var viewport = new Rect(0, 0, geometry.Width, geometry.Height);
                _recorder.RecordFrame(frame, viewport, viewport, 0);
                _clock.Delay(100, source.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _recorder.RecordEvent("tail-unavailable", new { Reason = ex.Message }); }
        finally { lock (_lifecycle) _tailCancellation = null; }
    }

    private void ClickHotbarSlot(IntPtr robloxHwnd, int clientW, int clientH, int slotNum)
    {
        slotNum = Math.Clamp(slotNum, 1, 9);
        int slotClientX = -1;
        int slotClientY = -1;

        // Use OpenCV dynamic hotbar locator to target slot with sub-pixel precision
        try
        {
            int bottomH = Math.Max(50, (int)Math.Round(clientH * 0.25));
            int bottomY = clientH - bottomH;
            using var bottomSnap = _shakeCapture.CaptureClientRegion(robloxHwnd, 0, bottomY, clientW, bottomH);
            if (bottomSnap != null && !bottomSnap.Empty())
            {
                using var bgr = new Mat();
                if (bottomSnap.Channels() == 4)
                    Cv2.CvtColor(bottomSnap, bgr, ColorConversionCodes.BGRA2BGR);
                else
                    bottomSnap.CopyTo(bgr);

                var rodRes = _vision.DetectRodEquipped(bgr, slotNum, generateDebug: false, fullViewportHeight: clientH);
                if (rodRes.HotbarFound && rodRes.GeometryConfirmed)
                {
                    slotClientX = rodRes.SlotCenter.X;
                    slotClientY = bottomY + rodRes.SlotCenter.Y;
                }
            }
        }
        catch (GameplayInterruptedException) { throw; }
        catch { }

        if (slotClientX < 0 || slotClientY < 0)
            throw new GameplayInterruptedException("Hotbar target could not be detected. No estimated click was sent.");

        if (_desktop.SanitizeGameCoordinate(robloxHwnd, slotClientX, slotClientY, out int safeX, out int safeY, out int sX, out int sY))
        {
            SessionLogger.Instance.Log("ROD", $"ClickHotbarSlot {slotNum}: Dynamic Client=({safeX}, {safeY}) -> Screen=({sX}, {sY})");
            _input.SendHardwareClick(sX, sY, safeX, safeY, robloxHwnd);
        }
    }

    public bool IsRodEquipped(IntPtr robloxHwnd, int clientW, int clientH)
    {
        _lastHotbarGeometryConfirmed = false;
        try
        {
            int slotNum = 1;
            if (!string.IsNullOrEmpty(Config.RodSlot) && char.IsDigit(Config.RodSlot[0]))
            {
                slotNum = Math.Clamp(Config.RodSlot[0] - '0', 1, 9);
            }

            // Capture the bottom ~25% of Roblox window for dynamic hotbar detection
            int bottomH = Math.Max(50, (int)Math.Round(clientH * 0.25));
            int bottomY = clientH - bottomH;
            using var bottomSnap = _shakeCapture.CaptureClientRegion(robloxHwnd, 0, bottomY, clientW, bottomH);
            if (bottomSnap == null || bottomSnap.Empty())
            {
                _lastKnownRodEquipped = false;
                _lastKnownRodStatus = "ROD: NOT DETECTED";
                return false;
            }

            using var bgr = new Mat();
            if (bottomSnap.Channels() == 4)
                Cv2.CvtColor(bottomSnap, bgr, ColorConversionCodes.BGRA2BGR);
            else
                bottomSnap.CopyTo(bgr);

            var rodRes = _vision.DetectRodEquipped(bgr, slotNum, generateDebug: false, fullViewportHeight: clientH);
            _lastHotbarGeometryConfirmed = rodRes.GeometryConfirmed;
            _lastKnownRodEquipped = rodRes.IsEquipped;
            _lastKnownRodStatus = rodRes.IsEquipped ? "ROD: EQUIPPED" : "ROD: UNEQUIPPED";
            return rodRes.IsEquipped;
        }
        catch (GameplayInterruptedException) { throw; }
        catch
        {
            _lastKnownRodEquipped = false;
            _lastKnownRodStatus = "ROD: STANDBY";
            return false;
        }
    }

    private void EnsureRodEquipped(IntPtr robloxHwnd, int clientW, int clientH, bool force = false)
    {
        if (IsRodEquipped(robloxHwnd, clientW, clientH))
        {
            SessionLogger.Instance.Log("ROD", "EnsureRodEquipped: Rod is ALREADY in hand. No action taken.");
            return;
        }

        SessionLogger.Instance.Log("ROD", "EnsureRodEquipped: Rod is NOT in hand. Equipping rod now...");

        if (!_lastHotbarGeometryConfirmed)
            throw new GameplayInterruptedException("Hotbar is not visible; waiting to detect the reel or rod slot.");

        char rodKey = (!string.IsNullOrEmpty(Config.RodSlot) && char.IsDigit(Config.RodSlot[0])) ? Config.RodSlot[0] : '1';
        int slotNum = Math.Clamp(rodKey - '0', 1, 9);

        // Ensure Roblox has focus before sending hotkey
        ValidateGameplay();
        Delay(30);

        // Send hotkey once
        _input.SendKeyPress(rodKey);
        Delay(200);

        if (IsRodEquipped(robloxHwnd, clientW, clientH))
        {
            SessionLogger.Instance.Log("ROD", "EnsureRodEquipped: Rod successfully equipped via keypress.");
            return;
        }

        // If keypress did not equip or force requested, click the slot directly
        SessionLogger.Instance.Log("ROD", $"EnsureRodEquipped: Keypress did not equip. Clicking hotbar slot {slotNum} dynamically...");
        ClickHotbarSlot(robloxHwnd, clientW, clientH, slotNum);
        Delay(250);

        if (IsRodEquipped(robloxHwnd, clientW, clientH))
        {
            SessionLogger.Instance.Log("ROD", "EnsureRodEquipped: Rod successfully equipped via hardware click.");
        }
        else
        {
            throw new GameplayInterruptedException("Rod equip was not visually confirmed.");
        }

        // Return cursor to safe water
        EnsureCursorInWater(robloxHwnd, new Win32.RECT { Left = 0, Top = 0, Right = clientW, Bottom = clientH });
    }

    /// <summary>
    /// Recognize an existing reel in fresh frames before issuing cast/recovery inputs.
    /// </summary>
    private bool TryResumeVisibleReel()
    {
        ValidateGameplay();
        int width = _activeGeometry.Width, height = _activeGeometry.Height;
        int halfWidth = Math.Min(width / 2, (int)(height * .55));
        int left = width / 2 - halfWidth;
        int top = (int)(height * .74), bottom = (int)(height * .94);
        bool confirmed = ReelEntryGuard.Confirm(() =>
        {
            using var frame = _capture.CaptureClientRegion(_activeWindow, left, top, halfWidth * 2, bottom - top);
            if (frame == null || frame.Empty()) throw new GameplayInterruptedException("Invalid reel capture.");
            return _vision.ProcessTrack(frame, left, top, height / 1080.0, Config.SelectedTheme, false);
        }, Delay, _operationCancellation);
        if (!confirmed) return false;
        SetMouseDown(false);
        // Resize/focus recovery can leave the cursor on a title bar or outside the
        // client. Re-anchor before resuming button edges for the existing reel.
        EnsureCursorInGameView(_activeWindow, _activeGeometry);
        _lastProgressTicks = _clock.Timestamp;
        _lastKnownRodEquipped = true;
        _lastKnownRodStatus = "ROD: REELING";
        _recorder.RecordEvent("reel-entry", new { Evidence = "Fish and reel bar or progress detected in two fresh frames before cast/recovery" });
        Transition(MacroState.Reeling, "Existing reel detected; skipping cast/re-equip");
        return true;
    }

    public void RecoverAndRestart(string reason)
    {
        if (!_coordinator.IsOwner) { _ = _coordinator.Enqueue(() => { RecoverAndRestart(reason); return true; }); return; }
        if (!IsRunning || CurrentState == MacroState.Stopped || (_cts != null && _cts.IsCancellationRequested)) return;

        // A missed cast meter can still lead to a bite. Never toggle the rod during that reel.
        // Keep actual reel-stall recovery bounded; a frozen reel must not reset its timeout forever.
        if (CurrentState != MacroState.Reeling && TryResumeVisibleReel()) return;

        // A timed-out cycle is finished too: a queued stop must not start another cast.
        if (IsStopQueued)
        {
            Stop("Queued stop after cycle timeout");
            return;
        }

        lock (_recoveryLock)
        {
            if (_isRecovering) return;
            _isRecovering = true;
        }

        try
        {
            if (!_recoveryBudget.TryBegin())
            {
                throw new FishingSafetyException("Three recovery attempts produced no confirmed catch. Check the fishing position and restart when ready.");
            }
            _recorder.RecordEvent("recovery-attempt", new { Reason = reason, Attempt = _recoveryBudget.Attempts, Outcome = "Unknown" });
            lock (_statistics) WatchdogRecoveryCount++;
            _lifetimeRecoveries++;
            SessionLogger.Instance.Log("WATCHDOG", $"🚨 SELF-HEALING RECOVERY TRIGGERED: {reason} (Total recoveries: {WatchdogRecoveryCount})");

            var telemRecover = new TelemetryData
            {
                State = CurrentState,
                Action = $"🔄 SELF-HEALING: {reason}"
            };
            PopulateTelemetryStats(telemRecover);
            OnTelemetry?.Invoke(telemRecover);

            // Release every input owned by the macro before attempting recovery.
            _input.ReleaseAll();
            _isMouseDown = false;
            Delay(50);

            // Do not blindly toggle Escape: on unobstructed gameplay it opens the pause menu.

            IntPtr robloxHwnd = _desktop.FindRobloxWindow();
            if (robloxHwnd != IntPtr.Zero && _desktop.GetClientRect(robloxHwnd, out Win32.RECT clientRect) && clientRect.Width > 0 && clientRect.Height > 0)
            {
                int winW = clientRect.Width;
                int winH = clientRect.Height;

                // 3. Force Roblox into foreground
                ValidateGameplay();
                Delay(150);

                // 4. Dismiss equipment bag if open
                if (IsBagOpen(robloxHwnd, winW, winH))
                {
                    SessionLogger.Instance.Log("WATCHDOG", "Equipment bag was open. Closing bag via 'g'...");
                    _input.SendKeyPress('g');
                    Delay(250);
                }

                // 5. Force re-equip rod
                EnsureRodEquipped(robloxHwnd, winW, winH, force: true);
                Delay(200);

                // 6. Ensure cursor is aimed safely at open water
                EnsureCursorInWater(robloxHwnd, clientRect);
                Delay(150);
            }

            // 7. Reset progress ticks
            _lastProgressTicks = _clock.Timestamp;

            // 8. Transition cleanly to Casting
            Transition(MacroState.Casting, $"Recovery attempt dispatched; awaiting a confirmed catch: {reason}");
            SessionLogger.Instance.Log("WATCHDOG", "Recovery inputs dispatched; success is not yet verified.");
        }
        catch (FishingSafetyException) { throw; }
        catch (GameplayInterruptedException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            SessionLogger.Instance.LogError("RecoverAndRestart error", ex);
        }
        finally
        {
            lock (_recoveryLock)
            {
                _isRecovering = false;
            }
        }
    }

    public void ReEquipRod(bool clickSlot = false)
    {
        if (!_coordinator.IsOwner) { _ = _coordinator.Enqueue(() => RunManual(() => { ReEquipRod(clickSlot); return true; }, false)); return; }
        IntPtr robloxHwnd = _desktop.FindRobloxWindow();
        if (robloxHwnd == IntPtr.Zero || !_desktop.GetClientRect(robloxHwnd, out Win32.RECT clientRect) || clientRect.Width <= 0 || clientRect.Height <= 0)
        {
            return;
        }

        if (_desktop.GetForegroundWindow() != robloxHwnd)
        {
            ValidateGameplay();
            Delay(200);
        }

        int winW = clientRect.Width;
        int winH = clientRect.Height;

        if (clickSlot)
        {
            char slotChar = (!string.IsNullOrEmpty(Config.RodSlot) && char.IsDigit(Config.RodSlot[0])) ? Config.RodSlot[0] : '1';
            int slotNum = Math.Clamp(slotChar - '0', 1, 9);
            ClickHotbarSlot(robloxHwnd, winW, winH, slotNum);
        }
        else
        {
            EnsureRodEquipped(robloxHwnd, winW, winH, force: true);
        }
    }

    public void ExecuteTestCast()
    {
        if (!_coordinator.IsOwner) { _ = _coordinator.Enqueue(() => RunManual(() => { ExecuteTestCast(); return true; }, false)); return; }
        IntPtr robloxHwnd = _desktop.FindRobloxWindow();
        if (robloxHwnd == IntPtr.Zero || !_desktop.GetClientRect(robloxHwnd, out Win32.RECT clientRect) || clientRect.Width <= 0 || clientRect.Height <= 0)
        {
            return;
        }

        ReEquipRod(clickSlot: true);
        EnsureCursorInWater(robloxHwnd, clientRect);

        SetMouseDown(true);
        Delay(Config.CastHoldMs);
        SetMouseDown(false);
    }

    /// <summary>
    /// Executes a single vision-calibrated test cast, tracks the rising power bar in real-time,
    /// measures the exact millisecond duration to hit 100% peak, updates Config.CastHoldMs,
    /// and returns the calibrated milliseconds (or -1 if window/bar was not detected).
    /// </summary>
    public int ExecuteAutoTuneCast(bool recordReplication = true)
    {
        if (!_coordinator.IsOwner) return RunQueued(() => ExecuteAutoTuneCast(recordReplication), Config.CastHoldMs);
        IntPtr robloxHwnd = _desktop.FindRobloxWindow();
        if (robloxHwnd == IntPtr.Zero || !_desktop.GetClientRect(robloxHwnd, out Win32.RECT clientRect) || clientRect.Width <= 0 || clientRect.Height <= 0)
        {
            return -1;
        }

        if (recordReplication)
        {
            if (!_recorder.IsRecording) _recorder.StartSession(clientRect.Width, clientRect.Height, new { Mode = "Cast observation" });
            LastCastReplicationDir = _recorder.CurrentSessionDirectory;
        }
        ReEquipRod(clickSlot: true);
        EnsureCursorInWater(robloxHwnd, clientRect);

        int winW = clientRect.Width;
        int winH = clientRect.Height;



        SetMouseDown(true);
        long startTicks = _clock.Timestamp;
        int calibratedMs = -1;
        int maxHoldDuration = Math.Max(1150, Config.CastHoldMs + 250);

        // Center-anchored height-scaled ROI for ultra-fast BitBlt capture (~1-2ms instead of ~50ms full-window)
        // Works identically on 16:9, 16:10, 21:9 Ultrawide, and 32:9 Super Ultrawide
        int roiHalfW = (int)Math.Round(winH * 0.28);
        int roiX = Math.Max(0, (winW / 2) - roiHalfW);
        int roiW = Math.Min(winW - roiX, roiHalfW * 2);
        int roiY = (int)Math.Round(winH * 0.25);
        int roiH = (int)Math.Round(winH * 0.50);

        double lastFill = -1.0;
        double lastFillElapsed = 0.0;
        double fillRate = 0.135;
        double leadMs = Config.CastPredictiveLeadMs > 0 ? Config.CastPredictiveLeadMs : 25.0;

        while (GetElapsedMs(startTicks) < maxHoldDuration && !_operationCancellation.IsCancellationRequested)
        {
            double elapsed = GetElapsedMs(startTicks);
            var frame = _shakeCapture.CaptureClientRegion(robloxHwnd, roiX, roiY, roiW, roiH);
            if (frame != null && !frame.Empty())
            {
                var castRes = _vision.DetectCastBarROI(frame, winH, roiX, roiY, Config.SelectedTheme, Config.PreviewEnabled);
                if (recordReplication)
                {
                    _recorder.RecordEvent("cast-observation", new { Stage = "HOLD", ElapsedMs = elapsed, castRes.Found, castRes.FillPercent });
                }

                if (castRes.Found)
                {
                    double currentFill = castRes.FillPercent;
                    if (lastFill >= 0.0 && currentFill > lastFill && (elapsed - lastFillElapsed) >= 12.0)
                    {
                        double instantRate = (currentFill - lastFill) / (elapsed - lastFillElapsed);
                        if (instantRate >= 0.04 && instantRate <= 0.35)
                        {
                            fillRate = fillRate * 0.35 + instantRate * 0.65;
                        }
                        lastFill = currentFill;
                        lastFillElapsed = elapsed;
                    }
                    else if (lastFill < 0.0 && currentFill > 0.0)
                    {
                        lastFill = currentFill;
                        lastFillElapsed = elapsed;
                    }

                    double predictedFill = currentFill + (fillRate * leadMs);

                    if (Config.PreviewEnabled && castRes.AnnotatedFrame != null)
                    {
                        var telem = new TelemetryData
                        {
                            State = MacroState.Casting,
                            Action = $"🎯 Auto-Tuning: {currentFill:F0}% (Pred: {predictedFill:F0}%)",
                            AnnotatedFrame = castRes.AnnotatedFrame
                        };
                        PopulateTelemetryStats(telem);
                        OnTelemetry?.Invoke(telem);
                    }

                    bool hitTarget = predictedFill >= 97.5 || currentFill >= 95.5;
                    bool capReached = castRes.WhiteTop > 0 && castRes.WhiteTop <= castRes.GreenY + 4;

                    if (elapsed >= 300 && (hitTarget || capReached))
                    {
                        calibratedMs = (int)Math.Round(elapsed);
                        frame.Dispose();
                        break;
                    }
                }
                frame.Dispose();
            }

            Delay(2);
        }

        SetMouseDown(false);
        long releaseMs = (long)GetElapsedMs(startTicks);
        if (calibratedMs > 0)
        {
            Config.CastHoldMs = calibratedMs;
            Config.Save();
        }

        // Post-release capture for 450ms
        if (recordReplication)
        {
            long postReleaseTarget = releaseMs + 450;
            while (GetElapsedMs(startTicks) < postReleaseTarget && !_operationCancellation.IsCancellationRequested)
            {
                double elapsed = GetElapsedMs(startTicks);
                var frame = _shakeCapture.CaptureClientRegion(robloxHwnd, 0, 0, winW, winH);
                if (frame != null && !frame.Empty())
                {
                    var castRes = _vision.DetectCastBar(frame, Config.SelectedTheme, false);
                    _recorder.RecordEvent("cast-observation", new { Stage = "RELEASED", ElapsedMs = elapsed, castRes.Found, castRes.FillPercent });
                    frame.Dispose();
                }
                Delay(20);
            }

            _recorder.RecordEvent("cast-outcome", new { Outcome = "Unknown", CalibratedMs = calibratedMs, ReleaseMs = releaseMs });
        }
        return calibratedMs;
    }

    private void SetMouseDown(bool down)
    {
        if (_isMouseDown != down)
        {
            _isMouseDown = down;
            IntPtr robloxHwnd = _desktop.FindRobloxWindow();
            int sx = -1, sy = -1, cx = -1, cy = -1;
            if (_desktop.GetCursorPos(out Win32.POINT pt))
            {
                sx = pt.X;
                sy = pt.Y;
                if (robloxHwnd != IntPtr.Zero && _desktop.ScreenToClient(robloxHwnd, ref pt))
                {
                    cx = pt.X;
                    cy = pt.Y;
                }
            }
            SessionLogger.Instance.LogInput(down ? "MouseDown" : "MouseUp", sx, sy, cx, cy);
            if (down)
            {
                _input.SendHardwareMouseDown(sx, sy, cx, cy, robloxHwnd);
            }
            else
            {
                _input.SendHardwareMouseUp(sx, sy, cx, cy, robloxHwnd);
            }
        }
    }

    private void Transition(MacroState newState, string reason = "")
    {
        // Also catch requests arriving during post-catch rest or optional reward work.
        if (newState == MacroState.Casting && IsStopQueued)
        {
            Stop(CurrentState == MacroState.PostCatch ? "Queued stop after catch" : "Queued stop before next cast");
            return;
        }
        MacroState oldState = CurrentState;
        _recorder.RecordEvent("transition", new { From = oldState.ToString(), To = newState.ToString(), Reason = reason });
        if (newState != MacroState.PostCatch)
        {
            _catchBannerSeen = false;
        }

        double elapsed = (_stateStartTime > 0) ? GetElapsedMs(_stateStartTime) : 0;
        SessionLogger.Instance.LogState(oldState, newState, $"{reason} (Elapsed in {oldState}: {elapsed:F0}ms)");

        SetMouseDown(false);
        CurrentState = newState;
        if (newState is MacroState.Casting or MacroState.PostCatch) _vision.ResetReelTheme();
        if (newState == MacroState.PostCatch) { _readyHotbarFrames = 0; }
        else { _catchBannerFrames = 0; }
        _stateStartTime = _clock.Timestamp;
        _lastBarSeenTime = _clock.Timestamp;
        _lastFishSeenTime = _clock.Timestamp;
        _lastTickTime = 0; // Velocity samples belong to this reel, never a previous cycle/session.
        _prevBarCenter = 0;
        _barVelocity = 0;
        _prevFishX = 0;
        _fishVelocity = 0;
        _luringConfirmCount = 0;
        _pulseState = false;
        _lastPulseTime = 0;
        _dutyAccumulator = 0.0;
        _prevBarVelocity = 0;
        _shakeMemoryX = 0;
        _shakeMemoryY = 0;
        _shakeRepeatCounter = 0;
    }

    private double GetElapsedMs(long startTicks)
    {
        return _clock.ElapsedMilliseconds(startTicks);
    }

    private void EnsureCursorInWater(IntPtr hwnd, Win32.RECT clientRect)
    {
        Win32.POINT clientTopLeft = new Win32.POINT { X = 0, Y = 0 };
        if (_desktop.ClientToScreen(hwnd, ref clientTopLeft))
        {
            int safeClientX = clientRect.Width / 2;
            int safeClientY = (int)Math.Round(clientRect.Height * 0.38);
            int safeScreenX = clientTopLeft.X + safeClientX;
            int safeScreenY = clientTopLeft.Y + safeClientY;

            _input.SendHardwareMouseMove(safeScreenX, safeScreenY, safeClientX, safeClientY, hwnd);
        }
    }

    private void EnsureCursorInGameView(IntPtr hwnd, Win32.RECT clientRect)
    {
        if (_desktop.GetCursorPos(out Win32.POINT mousePt))
        {
            Win32.POINT clientTopLeft = new Win32.POINT { X = 0, Y = 0 };
            if (_desktop.ClientToScreen(hwnd, ref clientTopLeft))
            {
                int screenLeft = clientTopLeft.X;
                int screenTop = clientTopLeft.Y;
                int screenRight = screenLeft + clientRect.Width;

                IntPtr winUnderCursor = _desktop.WindowFromPoint(mousePt);
                _desktop.GetWindowThreadProcessId(winUnderCursor, out uint curPid);
                uint macroPid = (uint)Environment.ProcessId;
                bool isCoveredByMacro = (curPid == macroPid);

                // Safe water zone: upper-middle gameplay area.
                // Lower 45% (Y > 0.55 * height) contains character avatar, boat deck, tooltips, and hotbars.
                // Upper 18% avoids the topbar menu.
                bool isInsideWater = (mousePt.X >= screenLeft + (int)(clientRect.Width * 0.20) &&
                                       mousePt.X <= screenRight - (int)(clientRect.Width * 0.20) &&
                                       mousePt.Y >= screenTop + (int)(clientRect.Height * 0.20) &&
                                       mousePt.Y <= screenTop + (int)(clientRect.Height * 0.52));

                if (isInsideWater && !isCoveredByMacro)
                {
                    // Cursor is already safely inside the open Roblox water area. Do not move it!
                    return;
                }

                // If cursor is outside safe water or occluded by macro, target center water area in front of avatar
                int safeX = screenLeft + (clientRect.Width / 2);
                int safeY = screenTop + (int)(clientRect.Height * 0.38);

                IntPtr winAtTarget = _desktop.WindowFromPoint(new Win32.POINT { X = safeX, Y = safeY });
                _desktop.GetWindowThreadProcessId(winAtTarget, out uint targetPid);
                if (targetPid == macroPid)
                {
                    safeX = screenLeft + Math.Clamp(clientRect.Width / 4, 80, 400);
                }

                _input.SendHardwareMouseMove(safeX, safeY, safeX - screenLeft, safeY - screenTop, hwnd);
            }
        }
    }

    private void CheckFishingView()
    {
        if (_viewChecked && GetElapsedMs(_lastViewCheck) < 1000) return;
        _viewChecked = true;
        _lastViewCheck = _clock.Timestamp;
        // Full context is needed for separated scene landmarks and pre-incident footage.
        // This runs once a second, not at the reel controller's capture frequency.
        for (int observation = 0; observation < 3; observation++)
        {
            using var frame = _shakeCapture.CaptureClientRegion(_activeWindow, 0, 0,
                _activeGeometry.Width, _activeGeometry.Height);
            if (frame == null || frame.Empty()) throw new GameplayInterruptedException("Invalid fishing view capture.");
            var status = _fishingView.Observe(frame);
            if (status is FishingViewStatus.Learning or FishingViewStatus.Stable) return;
            _input.ReleaseAll(); _isMouseDown = false;
            if (observation == 0)
                _recorder.RecordEvent("position-suspected", new { State = CurrentState.ToString(), Evidence = "Three scene landmarks changed", Outcome = "Unknown" });
            if (status == FishingViewStatus.Changed)
                throw new FishingSafetyException("Fishing view changed persistently. Check the player position and camera before restarting.");
            Delay(250); // Poll for persistent scene change; transient effects must not trigger a stop.
        }
    }

    private void WorkerLoop(CancellationToken ct)
    {
        Stopwatch loopSw = new Stopwatch();
        Stopwatch visionSw = new Stopwatch();



        while (!ct.IsCancellationRequested)
        {
            try
            {
                ValidateGameplay();
                CheckFishingView();
                if (CurrentState == MacroState.Casting || (CurrentState == MacroState.PostCatch && _catchOutcome.IsFinalized))
                    _coordinator.DrainPending();
                ValidateGameplay();
                if (Config.EnableWatchdogRecovery && GetElapsedMs(_lastProgressTicks) > Math.Max(25, Config.WatchdogStallTimeoutSeconds) * 1000)
                { RecoverAndRestart("Workflow made no progress before its deadline"); continue; }
                loopSw.Restart();
                long nowTicks = _clock.Timestamp;

            IntPtr robloxHwnd = _desktop.FindRobloxWindow();
            if (robloxHwnd == IntPtr.Zero || !_desktop.GetClientRect(robloxHwnd, out Win32.RECT clientRect) || clientRect.Width <= 0 || clientRect.Height <= 0)
            {
                var telem = new TelemetryData
                {
                    State = CurrentState,
                    Action = "Waiting for Roblox window..."
                };
                PopulateTelemetryStats(telem);
                OnTelemetry?.Invoke(telem);
                Delay(200);
                continue;
            }

            int winW = clientRect.Width;
            int winH = clientRect.Height;
            double scaleFactor = winH / 1080.0;

            // Compute minigame track coordinates (clamped to prevent ultrawide monitors from ballooning the crop)
            int halfW = (int)Math.Min(winW * 0.45, Math.Max(winH * 0.55, 450));
            halfW = Math.Min(halfW, (int)(winH * 0.65));
            int trackX1 = Math.Max(0, (winW / 2) - halfW);
            int trackX2 = Math.Min(winW, (winW / 2) + halfW);
            int trackY1 = (int)(winH * 0.74);
            int trackY2 = (int)(winH * 0.94); // Include catch progress to recognize dim/translucent reels.
            int trackW = trackX2 - trackX1;
            int trackH = trackY2 - trackY1;
            if (_adaptationTrackWidth != trackW || _adaptiveEnabled != Config.EnableAdaptiveRodDynamics)
            {
                _adaptiveEnabled = Config.EnableAdaptiveRodDynamics;
                _adaptationTrackWidth = trackW;
                double baseline = _baseRodPullAccel / 1188.0;
                var saved = BoundedRodEstimator.Load(AppDataPaths.FilePath("rod-profile.json"), Config.RodProfile, winW, winH);
                _rodEstimator = new BoundedRodEstimator(baseline, saved?.PullPerTrackSecondSquared);
                _releaseEstimator = new BoundedRodEstimator(_gravityFallAccel / 1188.0,
                    saved is { ReleaseSamples: >= 30, ReleasePerTrackSecondSquared: > 0 } ? saved.ReleasePerTrackSecondSquared : null);
                _estRodPullAccel = (Config.EnableAdaptiveRodDynamics ? _rodEstimator.Pull : baseline) * trackW;
            }

            // ==============================================================
            // STATE 1: CASTING
            // ==============================================================
            if (CurrentState == MacroState.Casting)
            {
                if (TryResumeVisibleReel()) continue;
                // Capture initial game preview frame so camera monitor is instantly live with dynamic rod vision
                Mat? castFrame = null;
                if (Config.PreviewEnabled)
                {
                    castFrame = _shakeCapture.CaptureClientRegion(robloxHwnd, 0, 0, winW, winH);
                    if (castFrame != null && !castFrame.Empty())
                    {
                        using var bgr = new Mat();
                        if (castFrame.Channels() == 4)
                            Cv2.CvtColor(castFrame, bgr, ColorConversionCodes.BGRA2BGR);
                        else
                            castFrame.CopyTo(bgr);

                        char rodKey = (!string.IsNullOrEmpty(Config.RodSlot) && char.IsDigit(Config.RodSlot[0])) ? Config.RodSlot[0] : '1';
                        int slotNum = Math.Clamp(rodKey - '0', 1, 9);
                        var rodRes = _vision.DetectRodEquipped(bgr, slotNum, generateDebug: true, fullViewportHeight: winH);
                        if (rodRes.AnnotatedFrame != null)
                        {
                            castFrame.Dispose();
                            castFrame = rodRes.AnnotatedFrame;
                        }
                    }
                }

                var telem = new TelemetryData
                {
                    State = MacroState.Casting,
                    Action = "Casting Rod...",
                    AnnotatedFrame = castFrame
                };
                PopulateTelemetryStats(telem);
                OnTelemetry?.Invoke(telem);

                // Ensure Roblox is foreground
                if (_desktop.GetForegroundWindow() != robloxHwnd)
                {
                    ValidateGameplay();
                    Delay(150);
                }

                // 1. Ensure the fishing rod is strictly equipped in hand before casting!
                EnsureRodEquipped(robloxHwnd, winW, winH);
                EnsureCursorInWater(robloxHwnd, clientRect);

                bool barEverFound = false;

                if (Config.EnableDynamicCastRelease)
                {
                    SetMouseDown(true);
                    long castStartTicks = _clock.Timestamp;
                    bool dynamicReleased = false;
                    int maxTimeoutMs = Math.Max(1200, Config.CastHoldMs + 250);

                    // Precompute ROI coordinates for ultra-fast BitBlt capture (~1-2ms instead of ~50ms full-window)
                    // Center-anchored height-scaled around player avatar (works on 16:9, 16:10, 21:9, and 32:9 Super Ultrawide)
                    int roiHalfW = (int)Math.Round(winH * 0.28);
                    int roiX = Math.Max(0, (winW / 2) - roiHalfW);
                    int roiW = Math.Min(winW - roiX, roiHalfW * 2);
                    int roiY = (int)Math.Round(winH * 0.25);
                    int roiH = (int)Math.Round(winH * 0.50);
                    double lastFill = -1.0;
                    double lastFillElapsed = 0.0;
                    double fillRate = 0.135; // Baseline velocity ~0.135% per ms (~740ms 0->100%)
                    double leadMs = Config.CastPredictiveLeadMs > 0 ? Config.CastPredictiveLeadMs : 25.0;

                    while (GetElapsedMs(castStartTicks) < maxTimeoutMs && !ct.IsCancellationRequested)
                    {
                        double elapsed = GetElapsedMs(castStartTicks);

                        // After initial ~100ms for bar animation to commence, scan at high speed
                        if (elapsed >= 100)
                        {
                            using var castRoiFrame = _shakeCapture.CaptureClientRegion(robloxHwnd, roiX, roiY, roiW, roiH);
                            if (castRoiFrame != null && !castRoiFrame.Empty())
                            {
                                var castResult = _vision.DetectCastBarROI(castRoiFrame, winH, roiX, roiY, Config.SelectedTheme, Config.PreviewEnabled);
                                if (castResult.Found && castResult.FillPercent > 0.0)
                                {
                                    barEverFound = true;
                                    double currentFill = castResult.FillPercent;

                                    // Compute rising fill velocity (% per ms)
                                    if (lastFill >= 0.0 && currentFill > lastFill && (elapsed - lastFillElapsed) >= 12.0)
                                    {
                                        double instantRate = (currentFill - lastFill) / (elapsed - lastFillElapsed);
                                        if (instantRate >= 0.04 && instantRate <= 0.35)
                                        {
                                            fillRate = fillRate * 0.35 + instantRate * 0.65;
                                        }
                                        lastFill = currentFill;
                                        lastFillElapsed = elapsed;
                                    }
                                    else if (lastFill < 0.0 && currentFill > 0.0)
                                    {
                                        lastFill = currentFill;
                                        lastFillElapsed = elapsed;
                                    }

                                    // Predictive lead calculation:
                                    // Estimates fill level at the exact instant mouse release takes effect in Roblox (~25ms later)
                                    double predictedFill = currentFill + (fillRate * leadMs);

                                    if (Config.PreviewEnabled && castResult.AnnotatedFrame != null)
                                    {
                                        var castTelem = new TelemetryData
                                        {
                                            State = MacroState.Casting,
                                            Action = $"⚡ CAST: {currentFill:F0}% (Pred: {predictedFill:F0}%)",
                                            AnnotatedFrame = castResult.AnnotatedFrame
                                        };
                                        PopulateTelemetryStats(castTelem);
                                        OnTelemetry?.Invoke(castTelem);
                                    }

                                    // Peak release trigger:
                                    // Target is 97%–99% sweet spot.
                                    // With ~25ms lead, predictedFill >= 97.5 dispatches mouse release when current fill is ~94-95%,
                                    // arriving precisely within the 97-99% window when processed by Roblox.
                                    // Safety fallback: if current fill already reached 95.5% or cap edge, release immediately.
                                    bool hitTarget = predictedFill >= 97.5 || currentFill >= 95.5;
                                    bool capReached = castResult.WhiteTop > 0 && castResult.WhiteTop <= castResult.GreenY + 4;

                                    if (elapsed >= 300 && (hitTarget || capReached))
                                    {
                                        SetMouseDown(false);
                                        dynamicReleased = true;
                                        int elapsedMs = (int)Math.Round(GetElapsedMs(castStartTicks));

                                        // Adaptive auto-tuning: gently update baseline CastHoldMs
                                        if (Config.EnableAutoTuneCastDelay && elapsedMs >= 450 && elapsedMs <= 1300)
                                        {
                                            Config.CastHoldMs = (int)Math.Round(Config.CastHoldMs * 0.75 + elapsedMs * 0.25);
                                            Config.Save();
                                        }

                                        var telemDone = new TelemetryData
                                        {
                                            State = MacroState.Casting,
                                            Action = $"🎯 PERFECT CAST! ({elapsedMs}ms, {currentFill:F1}%, pred: {predictedFill:F1}%)"
                                        };
                                        PopulateTelemetryStats(telemDone);
                                        OnTelemetry?.Invoke(telemDone);
                                        break;
                                    }
                                }
                            }
                        }

                        // Nominal fallback:
                        // If bar was detected, allow it to rise to peak up to CastHoldMs + 220ms
                        // If bar was never detected, release at nominal jittered CastHoldMs
                        int releaseCutoff = barEverFound
                            ? Math.Max(Config.CastHoldMs + 220, 1050)
                            : GetJitteredMs(Config.CastHoldMs, 8);

                        if (elapsed >= releaseCutoff && !dynamicReleased)
                        {
                            SetMouseDown(false);
                            break;
                        }

                        Delay(2);
                    }
                    SetMouseDown(false);
                }
                else
                {
                    SetMouseDown(true);
                    Delay(GetJitteredMs(Config.CastHoldMs, 8));
                    SetMouseDown(false);
                }

                // A missed meter is inconclusive: the cast may already be in flight.
                // Let normal shake/reel detection run until the bounded lure deadline
                // instead of sending another cast or recovery input immediately.
                if (Config.EnableDynamicCastRelease && !barEverFound)
                {
                    if (TryResumeVisibleReel()) continue;
                    SessionLogger.Instance.Log("CAST", "Cast meter not detected; waiting for shake/reel before deciding the cast failed.");
                    _lastProgressTicks = _clock.Timestamp;
                    Transition(MacroState.Luring, "Cast released without meter confirmation; waiting for bite");
                    continue;
                }

                _lastProgressTicks = _clock.Timestamp;

                // If bar was found, we are 100% certain the rod is in hand and bobber is in water!
                _lastKnownRodEquipped = true;
                _lastKnownRodStatus = "ROD: EQUIPPED";

                // Cast has finished holding and was released with the verified rod in hand.
                // The bobber is in flight/water. Proceed directly to Luring!
                SessionLogger.Instance.Log("CAST", $"Cast hold completed ({GetElapsedMs(_stateStartTime):F0}ms, barEverFound={barEverFound}). Bobber is in water. Transitioning to Luring.");
                // AFK mode watches for shake/reel immediately after release.
                if (!Config.AfkPerformanceMode) Delay(GetJitteredMs(Config.PostCastDelayMs, 40));
                Transition(MacroState.Luring, "Cast dispatched");
                continue;
            }

            // Preview work is independent of the controller cadence (at most 10 Hz).
            bool shouldGenerateDebug = Config.PreviewEnabled && GetElapsedMs(_lastDebugFrameTick) >= 100.0;
            if (shouldGenerateDebug)
            {
                _lastDebugFrameTick = nowTicks;
            }

            // ==============================================================
            // STATE 2: LURING (WAITING FOR BITE)
            // ==============================================================
            if (CurrentState == MacroState.Luring)
            {
                // Capture the FULL Roblox window for 100% game coverage and complete live preview
                visionSw.Restart();
                using Mat? fullFrame = _shakeCapture.CaptureClientRegion(robloxHwnd, 0, 0, winW, winH);
                visionSw.Stop();

                DetectionResult luringDetect = new DetectionResult();
                if (fullFrame != null && !fullFrame.Empty())
                {
                    // Crop reel minigame track from full frame to check if a bite occurred
                    int safeTrackX1 = Math.Clamp(trackX1, 0, winW - 1);
                    int safeTrackY1 = Math.Clamp(trackY1, 0, winH - 1);
                    int safeTrackW = Math.Clamp(trackW, 1, winW - safeTrackX1);
                    int safeTrackH = Math.Clamp(trackH, 1, winH - safeTrackY1);

                    using var trackCrop = new Mat(fullFrame, new Rect(safeTrackX1, safeTrackY1, safeTrackW, safeTrackH));
                    luringDetect = _vision.ProcessTrack(trackCrop, safeTrackX1, safeTrackY1, scaleFactor, Config.SelectedTheme, false);
                }

                if (luringDetect.BarFound || luringDetect.HasLiveReel)
                {
                    _luringConfirmCount++;
                    if (_luringConfirmCount >= 1)
                    {
                        _lastProgressTicks = _clock.Timestamp;
                        EnsureCursorInGameView(robloxHwnd, clientRect);
                        Transition(MacroState.Reeling, "Reel minigame bar detected");
                        continue;
                    }
                }
                else
                {
                    _luringConfirmCount = 0;
                }

                CheckAquariumClaimHeartbeat();

                // Timeout check: self-heal and re-cast if no bite within LureTimeoutMs
                if (GetElapsedMs(_stateStartTime) > Config.LureTimeoutMs)
                {
                    RecoverAndRestart($"Lure timeout ({Config.LureTimeoutMs / 1000}s without bite)");
                    continue;
                }

                // Bobber settle guard: allow 250ms for bobber to land in water before scanning/clicking
                bool bobberSettled = GetElapsedMs(_stateStartTime) >= 250;
                string luringAction = bobberSettled ? "Luring (Waiting for bite)..." : "Bobber landing in water...";
                Mat? luringDebugFrame = null;

                bool shakeActive = Config.EnableShakeClicks && Config.ShakeMode != "Disabled" && bobberSettled;
                if (fullFrame != null && !fullFrame.Empty())
                {
                    // Full-screen Shake detection with live annotated full game view preview
                    var shakeResult = shakeActive || shouldGenerateDebug
                        ? _vision.DetectShakeIcon(fullFrame, 0, 0, scaleFactor, shouldGenerateDebug)
                        : new ShakeDetectionResult();
                    luringDebugFrame = shakeResult.AnnotatedFrame;

                    if (shakeActive && GetElapsedMs(_lastShakeClickTime) >= GetJitteredMs(Config.ShakeClickIntervalMs, 4))
                    {
                        _lastShakeClickTime = nowTicks;

                        ValidateGameplay();

                        if (Config.ShakeMode == "Navigation")
                        {
                            // Roblox UI Navigation: Toggle UI focus (Backslash '\' VK_OEM_5 = 0xDC) and activate (Enter VK_RETURN = 0x0D)
                            _input.keybd_event(0xDC, 0, 0, 0); // Backslash Down
                            Delay(10);
                            _input.keybd_event(0xDC, 0, Win32.KEYEVENTF_KEYUP, 0); // Backslash Up
                            Delay(15);
                            _input.keybd_event(0x0D, 0, 0, 0); // Enter Down
                            Delay(10);
                            _input.keybd_event(0x0D, 0, Win32.KEYEVENTF_KEYUP, 0); // Enter Up
                            luringAction = "⚡ KEY SHAKE (Roblox UI Nav: \\ + Enter)";
                        }
                        else if (shakeResult.Found)
                        {
                            int clickClientX = shakeResult.Center.X;
                            int clickClientY = shakeResult.Center.Y;

                            // Memory check: verify if target has moved or is persistent
                            int dx = Math.Abs(clickClientX - _shakeMemoryX);
                            int dy = Math.Abs(clickClientY - _shakeMemoryY);
                            if (dx > 30 || dy > 30)
                            {
                                _shakeMemoryX = clickClientX;
                                _shakeMemoryY = clickClientY;
                                _shakeRepeatCounter = 0;
                            }
                            else
                            {
                                _shakeRepeatCounter++;
                            }

                            // Allow up to ~30 ticks (1.5 seconds) on the same shake circle before bypassing static screen elements
                            int maxBypassTicks = Math.Max(30, Config.ShakeRepeatBypass * 3);
                            if (_shakeRepeatCounter <= maxBypassTicks)
                            {
                                // Strictly clamp click coordinates within the Roblox game client area
                                int safeClickX = Math.Clamp(clickClientX, 0, winW - 1);
                                int safeClickY = Math.Clamp(clickClientY, 0, winH - 1);

                                // Convert client coords to physical screen coordinates
                                Win32.POINT screenPt = new Win32.POINT { X = safeClickX, Y = safeClickY };
                                if (_desktop.ClientToScreen(robloxHwnd, ref screenPt))
                                {
                                    IntPtr windowAtPoint = _desktop.WindowFromPoint(screenPt);
                                    _desktop.GetWindowThreadProcessId(windowAtPoint, out uint ptPid);
                                    uint macroPid = (uint)Environment.ProcessId;
                                    bool isOccludedByMacro = (ptPid == macroPid);

                                    if (!isOccludedByMacro)
                                    {
                                        // Execute high-reliability hardware click (DirectInput, RawInput, SendInput, PostMessage)
                                        SendShakeClick(screenPt.X, screenPt.Y, safeClickX, safeClickY, robloxHwnd);
                                        luringAction = $"⚡ SHAKE CLICK @ ({safeClickX},{safeClickY})";

                                        // Visual confirmation on live preview monitor
                                        if (luringDebugFrame != null && !luringDebugFrame.Empty())
                                        {
                                            Cv2.Circle(luringDebugFrame, new Point(clickClientX, clickClientY), 28, Scalar.FromRgb(255, 52, 52), 3);
                                            Cv2.PutText(luringDebugFrame, "CLICKED!", new Point(Math.Max(5, clickClientX - 35), Math.Max(25, clickClientY - 25)),
                                                HersheyFonts.HersheySimplex, 0.65, Scalar.FromRgb(255, 52, 52), 2);
                                        }
                                    }
                                    else
                                    {
                                        luringAction = "⚠️ Shake circle covered by macro window";
                                    }
                                }
                            }
                            else
                            {
                                // Persistent static target bypass
                                _shakeRepeatCounter = 0;
                                _shakeMemoryX = 0;
                                _shakeMemoryY = 0;
                                luringAction = "⚡ Bypassing static element...";
                            }
                        }
                        else
                        {
                            luringAction = "Watching for Shake Button...";
                        }
                    }
                }

                var luringTelem = new TelemetryData
                {
                    State = MacroState.Luring,
                    Action = luringAction,
                    VisionLatencyMs = visionSw.Elapsed.TotalMilliseconds,
                    AnnotatedFrame = luringDebugFrame
                };
                PopulateTelemetryStats(luringTelem);

                if (OnTelemetry != null)
                {
                    OnTelemetry.Invoke(luringTelem);
                }
                else
                {
                    luringTelem.Dispose();
                }

                CheckAntiAfkHeartbeat();

                // Throttle luring loop to a steady ~35 FPS to conserve CPU and provide smooth camera feed
                Delay(25);
                continue;
            }

            // Capture track crop directly from the Desktop DWM frame for REELING
            visionSw.Restart();
            using Mat? crop = _capture.CaptureClientRegion(robloxHwnd, trackX1, trackY1, trackW, trackH);
            if (crop == null || crop.Empty()) throw new GameplayInterruptedException("Invalid gameplay capture. Resume explicitly.");
            using DetectionResult detect = (crop != null)
                ? _vision.ProcessTrack(crop, trackX1, trackY1, scaleFactor, Config.SelectedTheme,
                    shouldGenerateDebug && CurrentState == MacroState.Reeling)
                : new DetectionResult();
            visionSw.Stop();
            // ==============================================================
            // STATE 3: REELING (ACTIVE COMPUTER VISION PURSUIT)
            // ==============================================================
            if (CurrentState == MacroState.Reeling)
            {
                // Deadlines must run even when every velocity sample is rejected as too slow.
                if (GetElapsedMs(_stateStartTime) > Config.ReelTimeoutMs)
                {
                    lock (_statistics) { UnknownCatches++; CurrentStreak = 0; }
                    _lifetimeUnknown++;
                    _recorder.RecordEvent("outcome", new { Outcome = "Unknown", Reason = "Stall Timeout" });
                    RecoverAndRestart($"Reel minigame stall ({Config.ReelTimeoutMs / 1000}s exceeded)");
                    continue;
                }
                double dtSec = (_lastTickTime > 0) ? (GetElapsedMs(_lastTickTime) / 1000.0) : 0.01;
                if (dtSec <= 0 || dtSec > 0.1)
                {
                    _prevBarCenter = _prevFishX = 0;
                    _barVelocity = _fishVelocity = _prevBarVelocity = 0;
                    SetMouseDown(false);
                    _lastTickTime = nowTicks;
                    detect.AnnotatedFrame?.Dispose();
                    Delay(1); // Yield one control tick after rejecting a timing sample; avoid a busy retry loop.
                    continue;
                }
                _lastTickTime = nowTicks;

                // Ensure Roblox is foreground
                if (_desktop.GetForegroundWindow() != robloxHwnd)
                {
                    ValidateGameplay();
                }

                // Start recorder session if enabled and not already active
                if (Config.EnableRecording && !_recorder.IsRecording)
                {
                    _recorder.StartSession(trackW, trackH);
                }

                if (detect.BarFound || detect.HasLiveReel)
                    _lastBarSeenTime = nowTicks;

                if (detect.BarFound)
                {
                    // Bar velocity smoothing (pixels per second)
                    if (_prevBarCenter > 0 && dtSec > 0)
                    {
                        double rawVel = (detect.BarCenter - _prevBarCenter) / dtSec;
                        rawVel = Math.Clamp(rawVel, -2.1 * trackW, 2.1 * trackW);
                        _barVelocity = (_barVelocity * 0.4) + (rawVel * 0.6);

                        // Online Adaptive Rod Dynamic Calibration:
                        // Measures actual rod pull acceleration during MouseDown to auto-tune to any rod
                        if (_isMouseDown && _barVelocity > _prevBarVelocity && detect.BarCenter < trackX2 - .067 * trackW && dtSec >= 0.01)
                        {
                            double measuredAccel = (_barVelocity - _prevBarVelocity) / dtSec;
                            _rodEstimator ??= new BoundedRodEstimator(_baseRodPullAccel / 1188.0);
                            _rodEstimator.Observe(measuredAccel / trackW, dtSec, detect.FishFound, visionSw.Elapsed.TotalMilliseconds <= 100);
                            if (Config.EnableAdaptiveRodDynamics) _estRodPullAccel = _rodEstimator.Pull * trackW;
                        }
                        else if (!_isMouseDown && _barVelocity < _prevBarVelocity && detect.BarCenter > trackX1 + .067 * trackW)
                            _releaseEstimator?.Observe((_prevBarVelocity - _barVelocity) / dtSec / trackW,
                                dtSec, detect.FishFound, visionSw.Elapsed.TotalMilliseconds <= 100);
                    }
                    else
                    {
                        _barVelocity = 0;
                    }
                    _prevBarCenter = detect.BarCenter;
                    _prevBarVelocity = _barVelocity;
                }

                // Fish target tracking with seamless trajectory extrapolation
                bool hasFishTarget = false;
                double targetFishX = 0;

                if (detect.FishFound)
                {
                    // Outlier rejection: if fish was recently tracked (< 250ms),
                    // reject sudden teleportations > 175px in a single tick as background clutter
                    double jumpDist = (_prevFishX > 0 && GetElapsedMs(_lastFishSeenTime) < 250)
                        ? Math.Abs(detect.FishX - _prevFishX)
                        : 0;

                    if (jumpDist > .147 * trackW)
                    {
                        // Momentary anomaly / reflection: extrapolate previous continuous trajectory
                        double timeSinceSeenSec = GetElapsedMs(_lastFishSeenTime) / 1000.0;
                        double dampFactor = Math.Max(0, 1.0 - (timeSinceSeenSec / 0.4));
                        targetFishX = _prevFishX + (_fishVelocity * timeSinceSeenSec * dampFactor);
                        targetFishX = Math.Clamp(targetFishX, trackX1 + 10, trackX2 - 10);
                        hasFishTarget = true;
                    }
                    else
                    {
                        _lastFishSeenTime = nowTicks;
                        hasFishTarget = true;
                        targetFishX = detect.FishX;

                        // Fish velocity smoothing
                        if (_prevFishX > 0 && dtSec > 0)
                        {
                            double rawFVel = (detect.FishX - _prevFishX) / dtSec;
                            rawFVel = Math.Clamp(rawFVel, -2.1 * trackW, 2.1 * trackW);
                            _fishVelocity = (_fishVelocity * 0.4) + (rawFVel * 0.6);
                        }
                        else
                        {
                            _fishVelocity = 0;
                        }
                        _prevFishX = detect.FishX;
                    }
                }
                else if (_prevFishX > 0 && GetElapsedMs(_lastFishSeenTime) < 400)
                {
                    // Extrapolate needle position during momentary occlusion (water splash, particles)
                    double timeSinceSeenSec = GetElapsedMs(_lastFishSeenTime) / 1000.0;
                    double dampFactor = Math.Max(0, 1.0 - (timeSinceSeenSec / 0.4));
                    targetFishX = _prevFishX + (_fishVelocity * timeSinceSeenSec * dampFactor);
                    targetFishX = Math.Clamp(targetFishX, trackX1 + 10, trackX2 - 10);
                    hasFishTarget = true;
                }

                string action = "";
                int barW = detect.BarWidth;

                // Dynamic Bar-Relative Geometry (Calibrated via active Rod Profile)
                double dynEdgeMargin = Math.Clamp(barW * _edgeMarginFactor, 20.0, 95.0);
                double dynDeadzone = Math.Clamp(barW * _deadzoneFactor, 8.0, 24.0);

                if (detect.BarFound && hasFishTarget)
                {
                    double error = targetFishX - detect.BarCenter;

                    // 1. CRITICAL OVERRIDE: Fish near edges of safe zone (immediate recovery)
                    if (targetFishX >= detect.BarRight - dynEdgeMargin)
                    {
                        SetMouseDown(true);
                        action = "EMERGENCY RIGHT >>";
                        _dutyAccumulator = 1.0;
                    }
                    else if (targetFishX <= detect.BarLeft + dynEdgeMargin)
                    {
                        SetMouseDown(false);
                        action = "<< EMERGENCY LEFT";
                        _dutyAccumulator = 0.0;
                    }
                    else
                    {
                        // 2. KINETIC PREDICTIVE CONTROLLER:
                        // Dynamic online-calibrated rod pull acceleration + measured gravity fall:
                        double aPull = _estRodPullAccel;
                        double aFall = (Config.EnableAdaptiveRodDynamics ? _releaseEstimator?.Pull ?? _gravityFallAccel / 1188.0
                            : _gravityFallAccel / 1188.0) * trackW;
                        double uHover = aFall / (aPull + aFall);

                        // Relative velocity in target (fish) frame of reference
                        double vRel = _barVelocity - _fishVelocity;

                        // Kinetic stopping distance relative to moving fish:
                        // When bar moves right faster than fish (vRel > 0), gravity coasting stops it in:
                        double coastDist = (vRel > 0) ? (vRel * vRel) / (2.0 * aFall) : 0;
                        // When bar moves left faster than fish (vRel < 0), pull braking stops it in:
                        double brakeDist = (vRel < 0) ? (vRel * vRel) / (2.0 * aPull) : 0;

                        // Anti-Overshoot Phase A: Coast into target from the left
                        // If the bar already has enough kinetic energy to coast right to the target, cut power!
                        if (error > dynDeadzone && coastDist >= (error - 3.0) && vRel > 0)
                        {
                            SetMouseDown(false);
                            action = "Coast Right ->";
                            _dutyAccumulator = 0.0;
                        }
                        // Anti-Overshoot Phase B: Brake into target from the right
                        // If the bar is dropping left towards the target and must brake now to avoid blowing past, apply full pull!
                        else if (error < -dynDeadzone && brakeDist >= (-error - 3.0) && vRel < 0)
                        {
                            SetMouseDown(true);
                            action = "<- Brake Left";
                            _dutyAccumulator = 1.0;
                        }
                        // Phase C: Proportional Velocity Tracking with Delta-Sigma Pulse Density Modulation
                        else
                        {
                            // Velocity feedforward + proportional position error
                            double targetVel = _fishVelocity + (5.0 * error);
                            targetVel = Math.Clamp(targetVel, -850.0, 850.0);
                            double velError = targetVel - _barVelocity;

                            // Continuous throttle command centered around hover equilibrium
                            double kv = 1.0 / (aPull + aFall);
                            double throttle = uHover + (velError * kv * 2.8);
                            throttle = Math.Clamp(throttle, 0.0, 1.0);

                            // First-order Delta-Sigma discrete pulse-density modulation
                            _dutyAccumulator += throttle;
                            bool doMouseDown = false;
                            if (_dutyAccumulator >= 1.0)
                            {
                                doMouseDown = true;
                                _dutyAccumulator -= 1.0;
                            }
                            else
                            {
                                doMouseDown = false;
                            }
                            SetMouseDown(doMouseDown);

                            if (Math.Abs(error) <= dynDeadzone)
                            {
                                action = $"Locked Center [{throttle:P0}]";
                            }
                            else if (velError > 60.0)
                            {
                                action = $"PULL RIGHT >> [{throttle:P0}]";
                            }
                            else if (velError < -60.0)
                            {
                                action = $"<< DROP LEFT [{throttle:P0}]";
                            }
                            else
                            {
                                action = $"Tracking [{(error >= 0 ? "+" : "")}{error:0}px]";
                            }
                        }
                    }
                }
                else if (detect.BarFound)
                {
                    action = "Searching for Needle...";
                    if (GetElapsedMs(_lastPulseTime) >= _pulseDuration)
                    {
                        _pulseState = !_pulseState;
                        _lastPulseTime = _clock.Timestamp;
                        _pulseDuration = _pulseState ? Config.HoverDownMs : Config.HoverUpMs;
                    }
                    SetMouseDown(_pulseState);
                }
                else
                {
                    action = "Bar Disappeared / Concluding...";
                    SetMouseDown(false);
                }

                // Dynamically check for in-game catch notification banner (confirmed catch)
                if (detect.HasLiveReel) { _catchBannerSeen = false; _catchBannerFrames = 0; }
                else if (crop != null)
                {
                    _catchBannerFrames = _vision.DetectCatchNotification(crop, winH) ? _catchBannerFrames + 1 : 0;
                    _catchBannerSeen |= _catchBannerFrames >= 2;
                }

                // A missing control bar is not a finished catch while the fish and progress remain visible.
                if (GetElapsedMs(_lastBarSeenTime) > 450)
                {
                    _catchOutcome = new CatchOutcomeTracker();
                    Transition(MacroState.PostCatch, "Verifying catch outcome");

                    detect.AnnotatedFrame?.Dispose();
                    continue;
                }

                // Render dynamic Action & Mouse HUD directly on the live camera frame
                if (detect.AnnotatedFrame != null && !detect.AnnotatedFrame.Empty())
                {
                    int fH = detect.AnnotatedFrame.Height;
                    int fW = detect.AnnotatedFrame.Width;
                    double errVal = (detect.BarFound && hasFishTarget) ? (targetFishX - detect.BarCenter) : 0;

                    // Top banner background
                    Cv2.Rectangle(detect.AnnotatedFrame, new Point(0, 0), new Point(fW, Math.Min(34, fH)), Scalar.FromRgb(15, 18, 26), -1);

                    // Action color coding
                    Scalar actionColor = action.Contains("EMERGENCY") ? Scalar.FromRgb(255, 23, 68)     // Bright Crimson Red
                                       : action.Contains("RIGHT")     ? Scalar.FromRgb(255, 82, 82)    // Coral Red
                                       : action.Contains("LEFT")      ? Scalar.FromRgb(33, 150, 243)    // Vibrant Blue
                                       : action.Contains("Locked")    ? Scalar.FromRgb(0, 230, 118)     // Emerald Green
                                       : action.Contains("Coast")     ? Scalar.FromRgb(171, 71, 188)    // Purple
                                       : action.Contains("Brake")     ? Scalar.FromRgb(255, 152, 0)     // Amber / Brake
                                       : Scalar.FromRgb(0, 229, 255);                                   // Cyan Tracking

                    string hudText = $"ACTION: {action} | MOUSE: {(_isMouseDown ? "[DOWN]" : "[UP]")} | ERR: {errVal:+0;-0;0}px";
                    Cv2.PutText(detect.AnnotatedFrame, hudText, new Point(12, 22), HersheyFonts.HersheySimplex, 0.55, actionColor, 2);

                    // Directional arrow on the right side
                    string arrow = action.Contains("EMERGENCY RIGHT") ? ">>>>"
                                 : action.Contains("EMERGENCY LEFT")  ? "<<<<"
                                 : action.Contains("RIGHT")           ? " >>>"
                                 : action.Contains("LEFT")            ? "<<< "
                                 : action.Contains("Coast")           ? "  -> "
                                 : action.Contains("Brake")           ? " <-  "
                                 : action.Contains("Locked")          ? " == "
                                 : " ~~ ";
                    Cv2.PutText(detect.AnnotatedFrame, arrow, new Point(fW - 95, 23), HersheyFonts.HersheyComplex, 0.65, actionColor, 2);
                }

                var telem = new TelemetryData
                {
                    State = MacroState.Reeling,
                    Action = action,
                    BarLeft = detect.BarLeft,
                    BarRight = detect.BarRight,
                    BarCenter = detect.BarCenter,
                    BarWidth = detect.BarWidth,
                    FishX = (int)Math.Round(targetFishX),
                    Error = (detect.BarFound && hasFishTarget) ? (targetFishX - detect.BarCenter) : 0,
                    BarVelocity = _barVelocity,
                    FishVelocity = _fishVelocity,
                    RodPullAccel = _estRodPullAccel,
                    LoopLatencyMs = loopSw.Elapsed.TotalMilliseconds,
                    VisionLatencyMs = visionSw.Elapsed.TotalMilliseconds,
                    IsMouseDown = _isMouseDown,
                    AnnotatedFrame = detect.TakeAnnotatedFrame()
                };
                PopulateTelemetryStats(telem);

                if (Config.EnableRecording && _recorder.IsRecording)
                {
                    _recorder.RecordTick(telem, _isMouseDown, detect.AnnotatedFrame);
                }

                if (OnTelemetry != null)
                {
                    OnTelemetry.Invoke(telem);
                }
                else
                {
                    telem.Dispose();
                }
            }
            // ==============================================================
            // STATE 4: POST CATCH DELAY
            // ==============================================================
            else if (CurrentState == MacroState.PostCatch)
            {
                if (detect.HasLiveReel && TryResumeVisibleReel())
                {
                    detect.AnnotatedFrame?.Dispose();
                    continue;
                }
                if (!_catchOutcome.IsFinalized)
                {
                    _catchBannerFrames = _vision.DetectCatchNotification(crop!, winH) ? _catchBannerFrames + 1 : 0;
                    _catchBannerSeen |= _catchBannerFrames >= 2;
                    _catchOutcome.Observe(_catchBannerSeen);
                    _readyHotbarFrames = IsRodEquipped(robloxHwnd, winW, winH) && _lastHotbarGeometryConfirmed
                        ? _readyHotbarFrames + 1 : 0;
                    var outcome = _catchOutcome.FinalizeOnce(
                        FishingCyclePolicy.CanFinalize(_catchBannerSeen, _readyHotbarFrames, GetElapsedMs(_stateStartTime)));
                    if (outcome == null) { Delay(20); continue; }
                    if (outcome == ActionOutcome.ConfirmedSuccess)
                    {
                        lock (_statistics) { TotalCatches++; CurrentStreak++; }
                        _lifetimeCatches++; _catchesSinceLastCrateOpen++;
                        _lastProgressTicks = _clock.Timestamp;
                        _recoveryBudget.ConfirmProgress();
                        _rodEstimator?.ConfirmCatch();
                        _releaseEstimator?.ConfirmCatch();
                        if (Config.EnableAdaptiveRodDynamics && _rodEstimator is { CanPersist: true })
                        {
                            var profile = new AdaptationProfile(1, Config.RodProfile, winW, winH,
                                _rodEstimator.Pull, _rodEstimator.Samples, _rodEstimator.ConfirmedCatches, DateTime.UtcNow,
                                _releaseEstimator is { CanPersist: true } ? _releaseEstimator.Pull : 0,
                                _releaseEstimator is { CanPersist: true } ? _releaseEstimator.Samples : 0);
                            string profilePath = AppDataPaths.FilePath("rod-profile.json");
                            File.WriteAllText(profilePath + ".tmp", System.Text.Json.JsonSerializer.Serialize(profile));
                            File.Move(profilePath + ".tmp", profilePath, true);
                        }
                    }
                    else if (outcome == ActionOutcome.ConfirmedFailure)
                    { lock (_statistics) { TotalFails++; CurrentStreak = 0; } _lifetimeFails++; }
                    else { lock (_statistics) { UnknownCatches++; CurrentStreak = 0; } _lifetimeUnknown++; }
                    _recorder.RecordEvent("outcome", new { Outcome = outcome.Value.ToString(), TotalCatches, TotalFails, UnknownCatches, SessionUptimeSeconds });
                }

                if (IsStopQueued)
                {
                    Stop("Queued stop after catch");
                    continue;
                }

                CheckAntiAfkHeartbeat();
                CheckAquariumClaimHeartbeat();

                if (_isAquariumClaimPending)
                {
                    var claimTelem = new TelemetryData
                    {
                        State = MacroState.PostCatch,
                        Action = "🏆 Claiming Aquarium Rewards..."
                    };
                    PopulateTelemetryStats(claimTelem);
                    OnTelemetry?.Invoke(claimTelem);

                    // Input/capture exceptions also leave this optional workflow disabled for the session.
                    _skipAquariumThisSession = true;
                    if (!ExecuteAquariumClaim())
                    {
                        _skipAquariumThisSession = !_aquariumCanRetryWithoutRecovery;
                        _isAquariumClaimPending = false;
                        if (!_aquariumCanRetryWithoutRecovery)
                        {
                            WaitForFishingRetry("Aquarium outcome unconfirmed; skipping automatic claims for this session");
                            continue;
                        }
                        SessionLogger.Instance.Log("AQUARIUM", "No reward inputs sent; continuing fishing and deferring aquarium retry.");
                    }
                    _skipAquariumThisSession = false;
                    Delay(400);
                }

                if (Config.EnableAutoOpenCrates && !_skipCratesThisSession && _catchesSinceLastCrateOpen >= Config.CrateIntervalCatches)
                {
                    var crateTelem = new TelemetryData
                    {
                        State = MacroState.PostCatch,
                        Action = "📦 Auto-Opening Caught Crates..."
                    };
                    PopulateTelemetryStats(crateTelem);
                    OnTelemetry?.Invoke(crateTelem);

                    _skipCratesThisSession = true;
                    bool cratesConfirmed = ExecuteAutoOpenCrates(Config.CrateMaxTypes, (msg) =>
                    {
                        var t = new TelemetryData { State = MacroState.PostCatch, Action = msg };
                        PopulateTelemetryStats(t);
                        OnTelemetry?.Invoke(t);
                    }, ct);

                    if (!cratesConfirmed)
                    {
                        _skipCratesThisSession = true;
                        WaitForFishingRetry("Crate outcome unconfirmed; skipping automatic crates for this session");
                        continue;
                    }
                    _skipCratesThisSession = false;
                    _catchesSinceLastCrateOpen = 0;
                    Delay(400);
                }

                Mat? postCatchFrame = null;
                if (Config.PreviewEnabled && shouldGenerateDebug)
                {
                    postCatchFrame = _shakeCapture.CaptureClientRegion(robloxHwnd, 0, 0, winW, winH);
                    if (postCatchFrame != null && !postCatchFrame.Empty())
                    {
                        using var bgr = new Mat();
                        if (postCatchFrame.Channels() == 4)
                            Cv2.CvtColor(postCatchFrame, bgr, ColorConversionCodes.BGRA2BGR);
                        else
                            postCatchFrame.CopyTo(bgr);

                        char rodKey = (!string.IsNullOrEmpty(Config.RodSlot) && char.IsDigit(Config.RodSlot[0])) ? Config.RodSlot[0] : '1';
                        int slotNum = Math.Clamp(rodKey - '0', 1, 9);
                        var rodRes = _vision.DetectRodEquipped(bgr, slotNum, generateDebug: true, fullViewportHeight: winH);
                        if (rodRes.AnnotatedFrame != null)
                        {
                            postCatchFrame.Dispose();
                            postCatchFrame = rodRes.AnnotatedFrame;
                        }
                    }
                }

                var telem = new TelemetryData
                {
                    State = MacroState.PostCatch,
                    Action = _catchBannerSeen ? "Catch confirmed. Resting..." : "Catch outcome unknown. Resting...",
                    AnnotatedFrame = postCatchFrame
                };
                PopulateTelemetryStats(telem);
                OnTelemetry?.Invoke(telem);

                if ((Config.AfkPerformanceMode && _readyHotbarFrames >= 2) || GetElapsedMs(_stateStartTime) >= Config.PostCatchDelayMs)
                {
                    _lastProgressTicks = _clock.Timestamp;
                    Transition(MacroState.Casting, "PostCatch delay elapsed");
                    continue;
                }
            }

            // Sleep remaining time to maintain ~100Hz (10ms tick rate)
            int elapsedLoop = (int)loopSw.ElapsedMilliseconds;
            int sleepTime = Math.Max(1, 10 - elapsedLoop);
            Delay(sleepTime);
            }
            catch (GameplayInterruptedException ex)
            { WaitForFishingRetry(ex.Message); }
            catch (FishingSafetyException) { throw; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SessionLogger.Instance.LogError("WorkerLoop Error", ex);
                try
                {
                    string errPath = AppDataPaths.FilePath("engine_error.log");
                    System.IO.File.AppendAllText(errPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [WorkerLoop Error] {ex}\n");
                }
                catch { }

                // Safe recovery: ensure mouse is released, wait briefly, and resume into Casting
                if (!ct.IsCancellationRequested)
                {
                    WaitForFishingRetry("Unexpected worker error: " + ex.GetType().Name);
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop("Engine disposal");
        _coordinator.Dispose();
        lock (_lifecycle) { _cts?.Dispose(); _cts = null; }
        _recorder.Dispose();
        _fishingView.Dispose();
        _shakeCapture.Dispose();
        _capture.Dispose();
    }
}
