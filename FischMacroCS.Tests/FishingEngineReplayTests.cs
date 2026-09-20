using FischMacroCS.Core;
using FischMacroCS.Native;
using OpenCvSharp;
using System.IO;
using System.Text.Json;

namespace FischMacroCS.Tests;

public class FishingEngineReplayTests
{
    [Fact]
    public async Task ResetWhileStoppedDoesNotStartTheSessionClock()
    {
        var clock = new Clock();
        using var frames = new Frames(() => throw new Exception("Reset must not capture"), clock);
        using var engine = new FishingEngine(new Settings { EnableRecording = false }, frames, frames,
            clock: clock, desktop: new Desktop(), hardware: new Input(clock));
        engine.ResetStats();
        await Task.Delay(30);
        Assert.Equal(0, engine.SessionUptimeSeconds);
        Assert.False(engine.IsRunning);
        Assert.Equal(0, engine.TotalCatches + engine.TotalFails + engine.UnknownCatches);
    }

    private sealed class Desktop : GameDesktop
    {
        public const int Width = 2254, Height = 1353;
        public int ClientWidth = Width;
        public uint Dpi = 96;
        public Win32.POINT Cursor = new() { X = Width / 2, Y = 500 };
        public IntPtr Window = (IntPtr)99;
        public bool Minimized;
        public int Restores;
        public int BeginPeriods, EndPeriods;
        public uint PointProcessId;
        public bool Focused = true;
        public int Activations;
        public int DenyNextActivations;
        public override IntPtr FindRobloxWindow() => Window;
        public override bool IsIconic(IntPtr window) => Minimized;
        public override IntPtr GetForegroundWindow() => Focused ? Window : (IntPtr)55;
        public override bool GetClientRect(IntPtr window, out Win32.RECT rect)
        { rect = new() { Right = ClientWidth, Bottom = Height }; return window != IntPtr.Zero && window == Window; }
        public override uint GetDpiForWindow(IntPtr window) => Dpi;
        public override void ForceSetForegroundWindow(IntPtr window)
        { Activations++; if (DenyNextActivations > 0) DenyNextActivations--; else Focused = true; }
        public override void ShowWindowAsync(IntPtr window, int command) { Minimized = false; Restores++; }
        public override bool ClientToScreen(IntPtr window, ref Win32.POINT point) => true;
        public override bool ScreenToClient(IntPtr window, ref Win32.POINT point) => true;
        public override bool GetCursorPos(out Win32.POINT point) { point = Cursor; return true; }
        public override IntPtr WindowFromPoint(Win32.POINT point) => Window;
        public override void GetWindowThreadProcessId(IntPtr window, out uint process) => process = PointProcessId;
        public override bool SanitizeGameCoordinate(IntPtr window, int x, int y, out int cx, out int cy, out int sx, out int sy)
        { cx = sx = x; cy = sy = y; return true; }
        public override void timeBeginPeriod(uint period) => BeginPeriods++;
        public override void timeEndPeriod(uint period) => EndPeriods++;
    }

    private sealed class Clock : IClock
    {
        public long Timestamp { get; private set; } = 1;
        public Action? Tick;
        public int CaptureMs = 5;
        public int RecoveryPollMs = 200;
        public bool WaitForCancellation;
        public TaskCompletionSource Waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void CaptureElapsed() => Timestamp += CaptureMs;
        public double ElapsedMilliseconds(long since) => Timestamp - since;
        public void Delay(int milliseconds, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Timestamp += milliseconds == 200 ? RecoveryPollMs : milliseconds;
            if (WaitForCancellation)
            {
                Waiting.TrySetResult();
                if (!cancellation.WaitHandle.WaitOne(TimeSpan.FromSeconds(5))) throw new TimeoutException("Shutdown did not cancel the worker wait");
                cancellation.ThrowIfCancellationRequested();
            }
            Tick?.Invoke(); cancellation.ThrowIfCancellationRequested();
        }
    }

    private sealed class Input(Clock clock) : IInputSink
    {
        public bool Held;
        public HashSet<byte> HeldKeys = new();
        public string? RejectOperation;
        public int RejectRemaining, Rejections;
        public List<string> Events = new();
        public List<(byte Key, uint Flags, long At)> KeyEdges = new();
        public List<(int X, int Y, bool Held, long At)> RelativeMoves = new();
        public Action? AfterRelative;
        public Action<byte, uint>? AfterKey;
        public Action<int, int>? AfterDown;
        private void Reject(string operation)
        {
            if (RejectOperation != operation || RejectRemaining <= 0) return;
            RejectRemaining--; Rejections++; Events.Add("rejected:" + operation);
            throw new GameplayInterruptedException("Injected hardware rejection: " + operation);
        }
        public void SendHardwareMouseDown(int sx = -1, int sy = -1, int cx = -1, int cy = -1, IntPtr window = default)
        { Reject("down"); Held = true; Events.Add("down"); AfterDown?.Invoke(cx, cy); }
        public void SendHardwareMouseUp(int sx = -1, int sy = -1, int cx = -1, int cy = -1, IntPtr window = default)
        { Reject("up"); Held = false; Events.Add("up"); }
        public void SendHardwareMouseMove(int sx, int sy, int cx = -1, int cy = -1, IntPtr window = default)
        { Reject("move"); Events.Add("move"); }
        public void SendHardwareClick(int sx, int sy, int cx = -1, int cy = -1, IntPtr window = default) => Events.Add("click");
        public void SetCursorPos(int x, int y) => Events.Add("cursor");
        public void SendRelativeMove(int x, int y)
        { Events.Add("relative"); RelativeMoves.Add((x,y,Held,clock.Timestamp)); AfterRelative?.Invoke(); }
        public void SendKeyPress(char key) => Events.Add("key:" + key);
        public void SendKeyString(string text, int delayMs = 40) => Events.Add("text:" + text);
        public void SelectAllAndClear() => Events.Add("clear");
        public void mouse_event(int flags, int x, int y, int data, int extra) => Events.Add("mouse:" + flags);
        public void keybd_event(byte key, byte scan, uint flags, int extra)
        {
            bool up = (flags & Win32.KEYEVENTF_KEYUP) != 0;
            Reject(up ? "key-up" : "key-down");
            if (up) HeldKeys.Remove(key); else HeldKeys.Add(key);
            Events.Add("key:" + key + ":" + flags); KeyEdges.Add((key, flags, clock.Timestamp)); AfterKey?.Invoke(key, flags);
        }
        public void ReleaseAll() { Held = false; Events.Add("release"); }
    }

    private sealed class Frames(Func<Mat> render, Clock clock) : IFrameSource
    {
        public Action<Rect>? BeforeCapture;
        public Mat? CaptureClientRegion(IntPtr window, int x, int y, int width, int height)
        { BeforeCapture?.Invoke(new Rect(x,y,width,height)); using var full = render(); clock.CaptureElapsed(); using var roi = new Mat(full, new Rect(x, y, width, height)); return roi.Clone(); }
        public void Dispose() { }
    }

    [Fact]
    public async Task ShutdownCancelsAnUnavailableWindowWaitAndJoinsTheWorker()
    {
        var desktop = new Desktop { Window = IntPtr.Zero };
        var clock = new Clock { WaitForCancellation = true };
        var input = new Input(clock);
        using var frames = new Frames(() => throw new Exception("Absent windows must never be captured"), clock);
        using var engine = new FishingEngine(new Settings { EnableRecording = false }, frames, frames, clock: clock, desktop: desktop, hardware: input);
        engine.Start();
        await clock.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(engine.IsRunning);
        await Task.Run(engine.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
        await engine.Completion;
        Assert.False(engine.IsRunning); Assert.False(input.Held);
        Assert.Null(engine.PauseReason);
        Assert.All(input.Events, action => Assert.Equal("release", action));
        Assert.Equal(1, desktop.BeginPeriods); Assert.Equal(1, desktop.EndPeriods);
        var completed = engine.Completion;
        engine.Start();
        Assert.Same(completed, engine.Completion); Assert.False(engine.IsRunning);
    }

    [Theory]
    [InlineData(true, false, null)]
    [InlineData(false, false, null)]
    [InlineData(true, true, null)]
    [InlineData(true, false, MacroState.Casting)]
    [InlineData(true, false, MacroState.Luring)]
    [InlineData(true, false, MacroState.Reeling)]
    [InlineData(true, false, MacroState.PostCatch)]
    [InlineData(true, true, MacroState.Stopped)] // Stop while recovery is waiting.
    public async Task RealWorkerCastsReelsFinalizesAndStartsNextCycle(bool catchVisible, bool interrupt, MacroState? stopDuring)
        => await RunWorkerScenario(catchVisible, interrupt, stopDuring);

    [Theory]
    [InlineData("resize")]
    [InlineData("dpi")]
    [InlineData("queued-restart")]
    [InlineData("reel-stall")]
    [InlineData("slow-reel")]
    [InlineData("missing-start")]
    [InlineData("replace-window")]
    [InlineData("minimize")]
    [InlineData("idle-enabled")]
    [InlineData("idle-disabled")]
    [InlineData("navigation-shake")]
    [InlineData("visual-shake")]
    [InlineData("visual-shake-stop")]
    [InlineData("visual-missing")]
    [InlineData("visual-occluded")]
    [InlineData("disabled-shake")]
    [InlineData("stop-shake")]
    [InlineData("failed-casts")]
    [InlineData("missing-bites")]
    [InlineData("recovery-budget")]
    [InlineData("reject-move")]
    [InlineData("reject-down")]
    [InlineData("reject-up")]
    [InlineData("reject-key-down")]
    [InlineData("reject-key-up")]
    [InlineData("reject-up-twice")]
    [InlineData("crates-unavailable")]
    [InlineData("aquarium-unavailable")]
    [InlineData("crates-unconfirmed")]
    [InlineData("aquarium-unconfirmed")]
    [InlineData("crates-input-error")]
    [InlineData("aquarium-input-error")]
    [InlineData("crates-stop")]
    [InlineData("aquarium-stop")]
    [InlineData("crates-overlay")]
    [InlineData("aquarium-overlay")]
    [InlineData("compact-reel")]
    [InlineData("companion-bonus")]
    [InlineData("maximized-catch")]
    [InlineData("stacked-catch")]
    [InlineData("server-update")]
    [InlineData("server-update-stop")]
    [InlineData("reconnect")]
    [InlineData("reconnect-retry")]
    [InlineData("reconnect-stop")]
    [InlineData("reconnect-stale")]
    [InlineData("continue")]
    [InlineData("continue-retry")]
    [InlineData("continue-stop")]
    [InlineData("continue-stale")]
    [InlineData("reconnect-chain")]
    [InlineData("reconnect-chain-focus")]
    [InlineData("reconnect-chain-stop")]
    public async Task PrimaryAfkScenariosKeepFishingUntilStopped(string scenario)
        => await RunWorkerScenario(true, false, null, scenario);

    [Theory]
    [InlineData("recording-recovery")]
    [InlineData("recording-disabled")]
    [InlineData("recording-storage-failure")]
    [InlineData("recording-stop")]
    [InlineData("recording-pressure")]
    [InlineData("recording-reset")]
    [InlineData("recording-reset-unknown")]
    public async Task RecordingPreservesEvidenceWithoutPreventingFishingOrShutdown(string scenario)
        => await RunWorkerScenario(scenario != "recording-reset-unknown", scenario == "recording-recovery",
            scenario == "recording-stop" ? MacroState.Reeling : null, scenario);

    private async Task RunWorkerScenario(bool catchVisible, bool interrupt, MacroState? stopDuring, string? scenario = null)
    {
        if (scenario == "companion-bonus") catchVisible = false;
        var desktop = new Desktop(); var clock = new Clock(); var input = new Input(clock);
        bool updateScenario = scenario is "server-update" or "server-update-stop";
        bool continueScenario = scenario?.StartsWith("continue", StringComparison.Ordinal) == true;
        bool chainScenario = scenario?.StartsWith("reconnect-chain", StringComparison.Ordinal) == true;
        bool reconnectScenario = continueScenario || scenario?.StartsWith("reconnect", StringComparison.Ordinal) == true;
        using var disconnectScreen = reconnectScenario ? Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", continueScenario ? "fisch_continue.png" : "disconnect_idle_278.png")) : new Mat();
        if (reconnectScenario) Assert.False(disconnectScreen.Empty());
        using var chainContinue = chainScenario ? Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fisch_continue.png")) : new Mat();
        if (chainScenario) Assert.False(chainContinue.Empty());
        int chainStage = 0; // Disconnect -> blank loading -> Continue -> blank loading -> gameplay.
        long chainContinueAt = long.MaxValue;
        var chainActions = new List<string>();
        var reconnectClicks = new List<long>();
        int reconnectCaptures = 0;
        bool reconnectHidden = false;
        using var updateScreen = updateScenario
            ? Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "server_update_wait.png")) : new Mat();
        if (updateScenario)
        {
            Assert.False(updateScreen.Empty());
            Assert.Equal(new Size(Desktop.Width, Desktop.Height), updateScreen.Size());
        }
        int updateFrames = 0, updateWaits = 0;
        bool recordingScenario = scenario?.StartsWith("recording-", StringComparison.Ordinal) == true;
        bool resetScenario = scenario is "recording-reset" or "recording-reset-unknown";
        bool statsReset = false;
        int beforeReset = -1, afterReset = -1;
        string? recordingDirectory = recordingScenario ? Path.Combine(Path.GetTempPath(), "fisch-recording-replay-" + Guid.NewGuid().ToString("N")) : null;
        if (scenario == "recording-storage-failure") File.WriteAllText(recordingDirectory!, "A file prevents creation of a recording directory.");
        bool rejectScenario = scenario?.StartsWith("reject-", StringComparison.Ordinal) == true;
        if (rejectScenario)
        {
            input.RejectOperation = scenario == "reject-up-twice" ? "up" : scenario![7..];
            input.RejectRemaining = scenario == "reject-up-twice" ? 2 : 1;
        }
        using var reel = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures",
            scenario == "compact-reel" ? "reel_live_false_exit.png" : "reel_active_recovery_21.png"));
        using var caught = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures",
            scenario == "companion-bonus" ? "companion_bonus_only.png" : scenario == "stacked-catch" ? "catch_stacked_rewards.png" : scenario == "maximized-catch" ? "catch_maximized_second.png" : "reel_catch_live.png"));
        if (scenario == "maximized-catch")
            Cv2.Resize(caught, caught, new Size((int)Math.Round(caught.Width * Desktop.Height / 1369.0), (int)Math.Round(caught.Height * Desktop.Height / 1369.0)));
        using var shakeStream = typeof(FishingEngine).Assembly.GetManifestResourceStream("FischMacroCS.Assets.shake_template.png")!;
        using var shakeBytes = new MemoryStream(); shakeStream.CopyTo(shakeBytes);
        using var shakeOriginal = Cv2.ImDecode(shakeBytes.ToArray(), ImreadModes.Color);
        using var shake = new Mat();
        Cv2.Resize(shakeOriginal, shake, new Size((int)Math.Round(shakeOriginal.Width * Desktop.Height / 1369.0),
            (int)Math.Round(shakeOriginal.Height * Desktop.Height / 1369.0)));
        Assert.False(reel.Empty()); Assert.False(caught.Empty());
        FishingEngine? engine = null;
        long reelStart = 0, unavailableUntil = 0;
        bool interrupted = false, sawLure = false, sawReel = false, sawPostCatch = false, nextCast = false;
        bool captureInterrupted = false;
        bool pressureInjected = false;
        bool windowChanged = false, stopQueued = false;
        int geometryInputStart = -1;
        int prematureCasts = 0;
        bool recoveredStall = false;
        bool workflowInputError = scenario is "crates-input-error" or "aquarium-input-error";
        bool workflowStop = scenario is "crates-stop" or "aquarium-stop";
        bool overlayScenario = scenario is "crates-overlay" or "aquarium-overlay";
        long? overlayReturnAt = null;
        int recoveryInputStart = -1;
        bool overlayReturned = false;
        bool workflowScenario = overlayScenario || workflowInputError || workflowStop || scenario is "crates-unavailable" or "aquarium-unavailable" or "crates-unconfirmed" or "aquarium-unconfirmed";
        bool unconfirmedWorkflow = overlayScenario || workflowInputError || workflowStop || scenario is "crates-unconfirmed" or "aquarium-unconfirmed";
        bool aquariumScenario = scenario?.StartsWith("aquarium-", StringComparison.Ordinal) == true;
        bool claimRequested = false;
        int workflowAttempts = 0;
        int postCatchPresses = 0;
        string? emptyWorkflowDirectory = workflowScenario ? Path.Combine(Path.GetTempPath(), "fisch-empty-workflow-" + Guid.NewGuid().ToString("N")) : null;
        using var workflowButton = new Mat();
        using var secondWorkflowButton = new Mat();
        if (unconfirmedWorkflow)
        {
            Directory.CreateDirectory(emptyWorkflowDirectory!);
            string[] names = aquariumScenario
                ? ["aquarium-navigation", "aquarium-claim", "aquarium-reward", "aquarium-close"]
                : ["equipment-button", "equipment-search", "crate-item"];
            for (int i = 0; i < names.Length; i++)
            {
                using var template = new Mat(24, 32, MatType.CV_8UC3, Scalar.All(30));
                Cv2.Rectangle(template, new Rect(3, 3, 24, 17), new Scalar(60 + i * 40, 200 - i * 30, 80 + i * 25), -1);
                Cv2.PutText(template, i.ToString(), new Point(7, 18), HersheyFonts.HersheySimplex, .5, Scalar.White, 1);
                Cv2.ImWrite(Path.Combine(emptyWorkflowDirectory!, names[i] + ".png"), template);
                if (i == 0) Cv2.Resize(template, workflowButton, new Size((int)(32 * Desktop.Height / 1080.0), (int)(24 * Desktop.Height / 1080.0)));
                if (i == 1) Cv2.Resize(template, secondWorkflowButton, new Size((int)(32 * Desktop.Height / 1080.0), (int)(24 * Desktop.Height / 1080.0)));
            }
        }
        bool retryScenario = scenario is "failed-casts" or "missing-bites" or "recovery-budget";
        long? cooldownStarted = null;
        bool shakeScenario = scenario is "navigation-shake" or "visual-shake" or "visual-shake-stop" or "visual-missing" or "visual-occluded" or "disabled-shake" or "stop-shake";
        long lureStart = 0;
        var shakePresses = new List<Point>();
        if (scenario == "visual-occluded") desktop.PointProcessId = (uint)Environment.ProcessId;
        bool stallScenario = scenario is "reel-stall" or "slow-reel";
        bool windowInterrupted = false, windowReturned = false;
        long returnWindowAt = scenario == "missing-start" ? 2000 : long.MaxValue;
        int missingInputIndex = 0;
        if (scenario == "missing-start") desktop.Window = IntPtr.Zero;
        int run = 1;
        long deadline = 20000;
        if (retryScenario || overlayScenario) deadline = 40000;
        bool idleScenario = scenario is "idle-enabled" or "idle-disabled";
        if (updateScenario) unavailableUntil = 6000;
        if (reconnectScenario) { unavailableUntil = continueScenario ? 7000 : 26000; deadline = 46000; }
        if (chainScenario) unavailableUntil = long.MaxValue; // Only the verified Continue click allows gameplay to return.
        if (idleScenario)
        {
            unavailableUntil = 26 * 60 * 1000;
            deadline += unavailableUntil;
            clock.RecoveryPollMs = 10000; // Advance idle intervals, preserving 50 ms heartbeat hold timing.
        }
        int stopIndex = -1;
        var inputViolations = new HashSet<string>();
        void StopAtBoundary() { stopIndex = input.Events.Count; engine!.Stop(); }
        var states = new HashSet<MacroState>();
        string lastAction = "";
        Mat Render()
        {
            var state = engine!.CurrentState; states.Add(state);
            if (scenario == "recording-pressure" && !pressureInjected && engine.Recorder.IsRecording)
            {
                pressureInjected = true;
                // Larger than the recorder's 48 MiB pending-frame budget: must drop before cloning.
                using var oversized = new Mat(4097, 4097, MatType.CV_8UC3, Scalar.Black);
                engine.Recorder.RecordFrame(oversized, new Rect(0, 0, 4097, 4097), new Rect(0, 0, 5000, 5000), 0);
            }
            if (aquariumScenario && state == MacroState.Reeling && !claimRequested)
            { claimRequested = true; engine.TriggerImmediateAquariumClaim(); }
            if (!windowInterrupted && state == MacroState.Reeling && scenario is "replace-window" or "minimize")
            {
                windowInterrupted = true;
                reelStart = clock.Timestamp + 3000;
                if (scenario == "replace-window")
                { desktop.Window = IntPtr.Zero; returnWindowAt = clock.Timestamp + 2500; missingInputIndex = input.Events.Count; }
                else { desktop.Minimized = true; desktop.Focused = false; }
            }
            if (!windowChanged && state == MacroState.Reeling && scenario is "resize" or "dpi")
            {
                windowChanged = true;
                geometryInputStart = input.Events.Count;
                desktop.Cursor = new() { X = desktop.ClientWidth - 60, Y = -15 }; // Native title-bar resize leaves cursor outside gameplay.
                reelStart = clock.Timestamp + 1400; // Reel remains live through the recovery cooldown.
                if (scenario == "resize") desktop.ClientWidth += 120;
                else desktop.Dpi = 144;
            }
            if (windowChanged && !sawPostCatch && state == MacroState.Casting) prematureCasts++;
            if (stallScenario && engine.WatchdogRecoveryCount > 0 && state == MacroState.Casting && !recoveredStall)
            { recoveredStall = true; reelStart = 0; }
            clock.CaptureMs = scenario == "slow-reel" && state == MacroState.Reeling && !recoveredStall ? 120 : 5;
            if (retryScenario) clock.CaptureMs = 30;
            if (shakeScenario && state == MacroState.Luring) clock.CaptureMs = 20;
            if (!stopQueued && run == 1 && state == MacroState.Reeling && scenario == "queued-restart")
            { engine.IsStopQueued = true; stopQueued = true; }
            var full = new Mat(Desktop.Height, desktop.ClientWidth, MatType.CV_8UC3, new Scalar(35, 20, 15));
            if (interrupt && !interrupted && state == MacroState.Luring)
            { interrupted = true; desktop.Focused = false; desktop.DenyNextActivations = 2; unavailableUntil = clock.Timestamp + 4500; }
            if (stopDuring == state && stopIndex < 0) StopAtBoundary();
            if (clock.Timestamp < unavailableUntil)
            {
                if (chainScenario && chainStage == 1 && clock.Timestamp >= chainContinueAt) chainStage = 2;
                if (reconnectScenario && !reconnectHidden && (!chainScenario || chainStage is 0 or 2))
                {
                    var recoveryScreen = chainScenario && chainStage == 2 ? chainContinue : disconnectScreen;
                    using var target = new Mat(full, new Rect((full.Width-recoveryScreen.Width)/2,
                        (full.Height-recoveryScreen.Height)/2, recoveryScreen.Width, recoveryScreen.Height));
                    recoveryScreen.CopyTo(target);
                }
                if (updateScenario)
                {
                    updateFrames++;
                    updateScreen.CopyTo(full);
                }
                if (interrupt && desktop.Focused && !captureInterrupted)
                {
                    captureInterrupted = true; full.Dispose();
                    throw new GameplayInterruptedException("Transient capture failure during recovery");
                }
                return full;
            }
            int left = desktop.ClientWidth / 2 - (69 * 8 + 68) / 2;
            void Paste(Mat source)
            {
                int top = (scenario == "compact-reel" && ReferenceEquals(source, reel)) || (scenario is "maximized-catch" or "stacked-catch" && ReferenceEquals(source, caught)) ? 1001 : 1015;
                using var target = new Mat(full, new Rect((full.Width-source.Width)/2, top, source.Width, source.Height));
                source.CopyTo(target);
            }
            bool hideCast = scenario == "failed-casts" && engine.WatchdogRecoveryCount == 0 ||
                scenario == "recovery-budget" && cooldownStarted == null;
            if (state == MacroState.Casting && input.Held && !hideCast)
            {
                // Synthetic near-full cast bar; real CV and predictive release still execute.
                Cv2.Rectangle(full, new Rect(full.Width / 2 - 10, 410, 20, 8), new Scalar(40, 220, 60), -1);
                Cv2.Rectangle(full, new Rect(full.Width / 2 - 5, 418, 10, 310), new Scalar(15, 15, 15), -1);
                Cv2.Rectangle(full, new Rect(full.Width / 2 - 5, 422, 10, 306), Scalar.White, -1);
            }
            if (state == MacroState.Luring)
            {
                sawLure = true;
                if (lureStart == 0) lureStart = clock.Timestamp;
                if (scenario == "missing-bites" && engine.WatchdogRecoveryCount == 0) { }
                else if (!shakeScenario || clock.Timestamp - lureStart >= 1200) Paste(reel);
                else if (scenario != "visual-missing")
                {
                    using var button = new Mat(full, new Rect(full.Width/2 - shake.Width/2, 650, shake.Width, shake.Height));
                    shake.CopyTo(button);
                }
            }
            if (state == MacroState.Reeling)
            {
                sawReel = true;
                if (reelStart == 0) reelStart = clock.Timestamp;
                if ((stallScenario && !recoveredStall) || clock.Timestamp - reelStart < 600) Paste(reel);
            }
            if (state == MacroState.PostCatch) { sawPostCatch = true; if (catchVisible || scenario == "companion-bonus") Paste(caught); }
            // Foreground hotbar must stay visible after composing recorded crops, which can overlap its band.
            Cv2.Rectangle(full, new Rect(left, Desktop.Height - 70, 68, 68), Scalar.White, 1);
            if (unconfirmedWorkflow && state == MacroState.PostCatch && workflowAttempts == 1)
            {
                bool openMenu = overlayScenario && postCatchPresses > 0;
                if (openMenu && overlayReturnAt.HasValue && clock.Timestamp >= overlayReturnAt.Value)
                { overlayReturned = true; return full; }
                int buttonX = Desktop.Width / 2 + (int)((aquariumScenario ? .0769 : 0) * Desktop.Height);
                int buttonY = (int)((aquariumScenario ? .0251 : .9) * Desktop.Height);
                var buttonImage = workflowButton;
                if (openMenu)
                {
                    full.SetTo(Scalar.All(20)); // Open menu hides the hotbar and reel, including during recovery.
                    buttonX = Desktop.Width / 2 + (int)((aquariumScenario ? -.184 : .0498) * Desktop.Height);
                    buttonY = (int)((aquariumScenario ? .5257 : .298) * Desktop.Height);
                    buttonImage = secondWorkflowButton;
                }
                using var target = new Mat(full, new Rect(buttonX - buttonImage.Width / 2, buttonY - buttonImage.Height / 2,
                    buttonImage.Width, buttonImage.Height));
                buttonImage.CopyTo(target); // Reward/result never appears after the second action.
            }
            return full;
        }
        using var frames = new Frames(Render, clock);
        frames.BeforeCapture = region =>
        {
            if (!reconnectScenario || region.Width != (int)(Desktop.Height * .62) || region.Height != (int)(Desktop.Height * .40)) return;
            reconnectCaptures++;
            if (scenario is "reconnect-stale" or "continue-stale" && reconnectCaptures == 2)
            { reconnectHidden = true; unavailableUntil = clock.Timestamp + 3000; }
        };
        using (engine = new FishingEngine(new Settings { EnableRecording = recordingScenario && scenario != "recording-disabled", EnableAntiAfk = scenario is "idle-enabled" or "reject-key-down" or "reject-key-up",
            EnableAutoClaimAquarium = workflowScenario && aquariumScenario, EnableAutoOpenCrates = workflowScenario && !aquariumScenario, CrateIntervalCatches = 1,
            ShakeMode = scenario is "navigation-shake" or "stop-shake" ? "Navigation" :
                scenario is "visual-shake" or "visual-shake-stop" or "visual-missing" or "visual-occluded" ? "Visual" : "Disabled",
            ReelTimeoutMs = stallScenario ? 1800 : 35000,
            LureTimeoutMs = scenario == "missing-bites" ? 1200 : 25000 },
            frames, frames, clock: clock, desktop: desktop, hardware: input, workflowTemplateDirectory: emptyWorkflowDirectory, recordingDirectory: recordingDirectory))
        {
            engine.OnTelemetry += t => {
                states.Add(t.State); lastAction = t.Action;
                if (t.Action.Contains("Auto-Opening Caught Crates", StringComparison.Ordinal) || t.Action.Contains("Claiming Aquarium Rewards", StringComparison.Ordinal))
                {
                    workflowAttempts++;
                    if (workflowInputError && workflowAttempts == 1) { input.RejectOperation = "down"; input.RejectRemaining = 1; }
                }
                if (t.Action.Contains("cooling down", StringComparison.OrdinalIgnoreCase)) cooldownStarted ??= clock.Timestamp;
                if (overlayScenario && !overlayReturnAt.HasValue && t.Action.Contains("outcome unconfirmed", StringComparison.OrdinalIgnoreCase))
                { overlayReturnAt = clock.Timestamp + 3000; recoveryInputStart = input.Events.Count; }
                t.Dispose();
            };
            input.AfterDown = (x,y) => {
                if (reconnectScenario && clock.Timestamp < unavailableUntil)
                {
                    reconnectClicks.Add(clock.Timestamp);
                    if (chainScenario && chainStage is not (0 or 2)) inputViolations.Add("Click during blank rejoin loading");
                    if (continueScenario || (chainScenario && chainStage == 2))
                    {
                        var promptSource = chainScenario ? chainContinue : disconnectScreen;
                        var expected = new Rect((Desktop.Width-promptSource.Width)/2+47,
                            (Desktop.Height-promptSource.Height)/2+281,310,36);
                        if (!expected.Contains(new Point(x,y))) inputViolations.Add($"Continue missed prompt: {x},{y}");
                    }
                    else if (Math.Abs(x - (Desktop.Width / 2 + 93)) > 15 || Math.Abs(y - (Desktop.Height / 2 + 87)) > 15)
                        inputViolations.Add($"Reconnect missed button: {x},{y}");
                    if (chainScenario)
                    {
                        if (chainStage == 0)
                        {
                            chainActions.Add("Reconnect"); chainStage = 1; chainContinueAt = clock.Timestamp + 1500;
                            if (scenario == "reconnect-chain-focus") { desktop.Focused = false; desktop.DenyNextActivations = 2; }
                        }
                        else if (chainStage == 2)
                        {
                            chainActions.Add("Continue"); chainStage = 3; reconnectHidden = true;
                            if (scenario == "reconnect-chain-stop") StopAtBoundary();
                            else unavailableUntil = clock.Timestamp + 3000;
                        }
                    }
                    else if (scenario is "reconnect-stop" or "continue-stop") StopAtBoundary();
                    else if (scenario is "reconnect" or "continue") { reconnectHidden = true; unavailableUntil = clock.Timestamp + 3000; }
                    else if (scenario == "continue-retry" && reconnectClicks.Count == 1)
                        unavailableUntil = clock.Timestamp + 7000;
                }
                if (engine.CurrentState == MacroState.Luring) shakePresses.Add(new Point(x,y));
                if (engine.CurrentState == MacroState.PostCatch) postCatchPresses++;
                if (workflowStop && engine.CurrentState == MacroState.PostCatch && stopIndex < 0) StopAtBoundary();
            };
            input.AfterKey = (key, flags) =>
            {
                if (scenario == "stop-shake" && key == 220 && flags == 0 && stopIndex < 0) StopAtBoundary();
            };
            input.AfterRelative = () =>
            {
                if (scenario == "visual-shake-stop" && input.Held && stopIndex < 0) StopAtBoundary();
            };
            clock.Tick = () =>
            {
                if (reconnectScenario && clock.Timestamp < unavailableUntil)
                {
                    if (input.KeyEdges.Any(edge => edge.Flags == 0)) inputViolations.Add("Key press before reconnect gameplay returned");
                    if (engine.TotalCatches + engine.TotalFails + engine.UnknownCatches != 0) inputViolations.Add("Outcome invented during reconnect wait");
                }
                if (updateScenario && clock.Timestamp < unavailableUntil)
                {
                    updateWaits++;
                    if (!engine.IsRunning) inputViolations.Add("Start cleared during server update");
                    foreach (var action in input.Events.Where(action => action is not "release" and not "up"))
                        inputViolations.Add("Server update waiting screen: " + action);
                    if (scenario == "server-update-stop" && clock.Timestamp >= 2000 && stopIndex < 0)
                        StopAtBoundary();
                }
                if (overlayScenario && overlayReturnAt.HasValue && clock.Timestamp < overlayReturnAt.Value)
                    foreach (var action in input.Events.Skip(recoveryInputStart).Where(action => action is not "release" and not "up"))
                        inputViolations.Add("Open menu during recovery: " + action);
                if (idleScenario && clock.Timestamp < unavailableUntil)
                    foreach (var action in input.Events.Where(action => action != "release" && action is not "key:126:0" and not "key:126:2"))
                        inputViolations.Add("Unavailable gameplay: " + action);
                if (stopDuring == MacroState.Stopped && interrupted && desktop.Activations >= 2 && stopIndex < 0)
                    StopAtBoundary();
                if (desktop.Window == IntPtr.Zero)
                {
                    // Window absence cannot send gameplay input, even when Start is persistent.
                    foreach (var action in input.Events.Skip(missingInputIndex).Where(action => action is not "release" and not "up"))
                        inputViolations.Add("Missing window: " + action);
                    if (clock.Timestamp >= returnWindowAt)
                    { desktop.Window = (IntPtr)100; windowReturned = true; }
                }
                if (engine.CurrentState == MacroState.Casting && sawPostCatch)
                {
                    if (resetScenario && !statsReset)
                    {
                        beforeReset = engine.TotalCatches + engine.UnknownCatches;
                        engine.ResetStats(); statsReset = true;
                        afterReset = engine.TotalCatches + engine.UnknownCatches;
                        sawPostCatch = false; reelStart = lureStart = 0;
                    }
                    else if (workflowScenario && engine.TotalCatches < 2)
                    { sawPostCatch = false; reelStart = lureStart = 0; }
                    else { nextCast = true; engine.Stop(); }
                }
                if (clock.Timestamp > deadline) engine.Stop(); // Bounded failure, not a passing timeout.
            };
            engine.Start();
            var firstWorker = engine.Completion;
            engine.Start();
            Assert.Same(firstWorker, engine.Completion); // Repeated Start cannot schedule a competing worker.
            try { await engine.Completion.WaitAsync(TimeSpan.FromSeconds(30)); }
            finally { engine.Stop(); }
            // Assert outside callbacks: worker recovery intentionally catches callback exceptions.
            Assert.Empty(inputViolations);
            Assert.Null(engine.PauseReason);
            if (reconnectScenario)
            {
                Assert.True(reconnectCaptures >= 2);
                Assert.Equal(chainScenario ? 2 : scenario is "reconnect-stale" or "continue-stale" ? 0 : scenario == "reconnect-retry" ? 3 : scenario == "continue-retry" ? 4 : 1, reconnectClicks.Count);
                if (chainScenario)
                {
                    Assert.Equal(new[] { "Reconnect", "Continue" }, chainActions);
                    Assert.True(reconnectClicks[1] >= chainContinueAt);
                    if (scenario == "reconnect-chain-focus") Assert.True(desktop.Activations >= 3);
                }
                else for (int i = 1; i < reconnectClicks.Count; i++) Assert.True(reconnectClicks[i] - reconnectClicks[i-1] >= (continueScenario ? 2000 : 10000));
            }
            if (updateScenario)
            {
                Assert.True(updateFrames > 1, "The recorded update screen must actually be observed repeatedly");
                Assert.True(updateWaits > 1, "The worker must retain Start through repeated waits");
                if (scenario == "server-update") Assert.True(clock.Timestamp >= unavailableUntil);
            }
            if (recordingScenario)
            {
                await Task.Run(engine.Dispose).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(engine.Recorder.IsRecording);
                Assert.False(input.Held); Assert.Empty(input.HeldKeys);
                if (scenario == "recording-disabled") Assert.False(Directory.Exists(recordingDirectory));
                else if (scenario == "recording-storage-failure") Assert.False(string.IsNullOrWhiteSpace(engine.Recorder.LastError));
                else
                {
                    Assert.Null(engine.Recorder.LastError);
                    string session = Assert.Single(Directory.GetDirectories(recordingDirectory!, "session_*"));
                    using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(session, "manifest.json")));
                    Assert.Equal(Desktop.Width, manifest.RootElement.GetProperty("Width").GetInt32());
                    using var completion = JsonDocument.Parse(File.ReadAllText(Path.Combine(session, "completed.json")));
                    Assert.Equal("Stopped", completion.RootElement.GetProperty("Outcome").GetString());
                    Assert.Equal("Requested", completion.RootElement.GetProperty("Statistics").GetProperty("StopSource").GetString());
                    Assert.Equal("Requested", engine.LastStopSource); // Dispose must preserve the initiating request.
                    Assert.Equal(resetScenario && catchVisible ? 2 : engine.TotalCatches, completion.RootElement.GetProperty("Statistics").GetProperty("TotalCatches").GetInt32());
                    if (resetScenario)
                    {
                        Assert.True(statsReset);
                        Assert.Equal(1, beforeReset); Assert.Equal(0, afterReset);
                        Assert.Equal(catchVisible ? 0 : 2, completion.RootElement.GetProperty("Statistics").GetProperty("UnknownCatches").GetInt32());
                        Assert.True(completion.RootElement.GetProperty("Statistics").GetProperty("SessionUptimeSeconds").GetDouble() > 0);
                    }
                    if (scenario == "recording-pressure")
                    {
                        Assert.True(pressureInjected);
                        Assert.True(completion.RootElement.GetProperty("DroppedEntries").GetInt64() >= 1);
                    }
                    using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(session, "frame-index.json")));
                    Assert.NotEmpty(index.RootElement.EnumerateArray());
                    foreach (var row in index.RootElement.EnumerateArray())
                    {
                        using var saved = Cv2.ImRead(Path.Combine(session, row.GetProperty("File").GetString()!));
                        Assert.False(saved.Empty());
                    }
                    if (scenario == "recording-recovery")
                    {
                        string journal = string.Join("\n", Directory.GetFiles(session, "*.jsonl").Select(File.ReadAllText));
                        Assert.Contains("recovery-context", journal);
                        Assert.Contains("fishing-retry", journal);
                    }
                }
            }
            if (scenario == "queued-restart")
            {
                Assert.True(stopQueued && sawPostCatch);
                Assert.Equal("Queued stop after catch", engine.LastStopSource);
                Assert.False(nextCast); // The queued stop must end before another cast.
                Assert.Equal(1, engine.TotalCatches);
                Assert.False(engine.IsStopQueued);
                Assert.False(input.Held);
                run++;
                sawLure = sawReel = sawPostCatch = nextCast = false;
                reelStart = 0;
                deadline = clock.Timestamp + 20000;
                engine.IsStopQueued = true; // Stale queued intent cannot leak into Start.
                engine.Start();
                try { await engine.Completion.WaitAsync(TimeSpan.FromSeconds(30)); }
                catch (TimeoutException ex)
                { throw new Exception($"Restart stuck: {engine.CurrentState}; clock={clock.Timestamp}; action={lastAction}; held={input.Held}; counts={engine.TotalCatches}/{engine.UnknownCatches}", ex); }
                finally { engine.Stop(); }
                Assert.Null(engine.PauseReason);
                Assert.Equal("Requested", engine.LastStopSource); // Previous run's queued reason must not leak.
            }
            if (stopDuring.HasValue || scenario is "stop-shake" or "visual-shake-stop" or "server-update-stop" or "reconnect-stop" or "continue-stop" or "reconnect-chain-stop" || workflowStop)
            {
                Assert.True(stopIndex >= 0, "Requested Stop boundary was never reached");
                Assert.All(input.Events.Skip(stopIndex), action => Assert.True(action is "release" or "up" || action.EndsWith(":2"), action));
                Assert.False(engine.IsRunning); Assert.False(input.Held);
                if (scenario == "visual-shake-stop") Assert.Single(input.RelativeMoves, move => move.Held);
                if (workflowStop)
                {
                    Assert.Equal(1, workflowAttempts); Assert.Equal(1, postCatchPresses);
                    Assert.Equal(ActionOutcome.Unknown, aquariumScenario ? engine.LastAquariumOutcome : engine.LastCrateOutcome);
                    Assert.Empty(input.HeldKeys);
                }
                if (scenario == "stop-shake")
                {
                    Assert.Single(input.KeyEdges, edge => edge.Key == 220 && edge.Flags == 0);
                    Assert.Single(input.KeyEdges, edge => edge.Key == 220 && edge.Flags == Win32.KEYEVENTF_KEYUP);
                    Assert.DoesNotContain(input.KeyEdges, edge => edge.Key == 13 && edge.Flags == 0);
                }
                return;
            }
            Assert.True(sawLure && sawReel && sawPostCatch && nextCast, $"States: {string.Join(',', states)}; clock={clock.Timestamp}; action={lastAction}");
            Assert.Equal(catchVisible ? (workflowScenario ? 2 : run) : 0, engine.TotalCatches);
            Assert.Equal(!catchVisible || stallScenario ? 1 : 0, engine.UnknownCatches);
            Assert.Equal(0, engine.TotalFails);
            Assert.False(engine.IsRunning); Assert.False(input.Held);
            Assert.Empty(input.HeldKeys);
            if (rejectScenario)
            {
                Assert.Equal(scenario == "reject-up-twice" ? 2 : 1, input.Rejections);
                Assert.Equal(0, input.RejectRemaining);
                if (scenario is "reject-key-down" or "reject-key-up")
                    Assert.Contains(input.KeyEdges, edge => edge.Key == 126 && edge.Flags == 0);
            }
            Assert.Contains("down", input.Events); Assert.Contains("up", input.Events);
            if (interrupt) { Assert.True(interrupted); Assert.True(captureInterrupted); Assert.True(desktop.Activations >= 4); }
            if (scenario is "resize" or "dpi")
            {
                Assert.True(windowChanged); Assert.Equal(0, prematureCasts);
                var recoveredInput = input.Events.Skip(geometryInputStart).ToList();
                int firstPress = recoveredInput.IndexOf("down");
                Assert.True(firstPress >= 0);
                Assert.Contains("move", recoveredInput.Take(firstPress));
            }
            if (stallScenario) { Assert.True(recoveredStall); Assert.Equal(1, engine.WatchdogRecoveryCount); }
            if (workflowScenario)
            {
                Assert.Equal(1, workflowAttempts); // Unavailable optional actions are skipped for the rest of this session.
                Assert.Equal(ActionOutcome.Unknown, aquariumScenario ? engine.LastAquariumOutcome : engine.LastCrateOutcome);
                Assert.Equal(overlayScenario ? 2 : unconfirmedWorkflow && !workflowInputError ? 1 : 0, postCatchPresses);
                if (workflowInputError) Assert.Equal(1, input.Rejections);
                if (scenario != "crates-overlay") Assert.Empty(input.KeyEdges);
                else Assert.Equal(input.KeyEdges.Count(e => e.Flags == 0), input.KeyEdges.Count(e => e.Flags == Win32.KEYEVENTF_KEYUP));
                if (overlayScenario)
                {
                    Assert.NotNull(overlayReturnAt); Assert.True(overlayReturned);
                    Assert.True(clock.Timestamp >= overlayReturnAt.Value);
                }
                if (scenario == "crates-unavailable") Assert.Contains("missing", engine.LastCrateEvidence!);
                if (scenario == "crates-unconfirmed") Assert.Contains("expected result not detected", engine.LastCrateEvidence!);
            }
            if (retryScenario)
            {
                Assert.Equal(scenario == "recovery-budget" ? 3 : 1, engine.WatchdogRecoveryCount);
                if (scenario == "recovery-budget")
                { Assert.NotNull(cooldownStarted); Assert.True(clock.Timestamp - cooldownStarted.Value >= 5000); }
            }
            if (scenario is "missing-start" or "replace-window") Assert.True(windowReturned);
            if (scenario == "minimize") { Assert.True(windowInterrupted); Assert.True(desktop.Restores > 0); Assert.False(desktop.Minimized); }
            if (shakeScenario)
            {
                if (scenario == "navigation-shake")
                {
                    foreach (byte key in new byte[] { 220, 13 })
                    {
                        int downs = input.KeyEdges.Count(edge => edge.Key == key && edge.Flags == 0);
                        Assert.True(downs > 0);
                        Assert.Equal(downs, input.KeyEdges.Count(edge => edge.Key == key && edge.Flags == Win32.KEYEVENTF_KEYUP));
                    }
                }
                else Assert.Empty(input.KeyEdges);
                if (scenario == "visual-shake")
                {
                    Assert.True(shakePresses.Count >= 2, "Repeated clicks on the same shake target must each move the mouse.");
                    Assert.Equal(shakePresses.Count * 6, input.RelativeMoves.Count);
                    foreach (var moves in input.RelativeMoves.Chunk(6))
                    {
                        Assert.Equal(new[] { (3,2,false), (-3,-2,false), (-2,2,false), (2,-2,false), (1,0,true), (-1,0,true) },
                            moves.Select(move => (move.X,move.Y,move.Held)).ToArray());
                        for (int i = 1; i < moves.Length; i++) Assert.True(moves[i].At > moves[i-1].At, "Relative motion must be separated for hover processing.");
                    }
                    Assert.All(shakePresses, point => {
                        Assert.InRange(Math.Abs(point.X - Desktop.Width/2), 0, 5);
                        Assert.InRange(Math.Abs(point.Y - (650 + shake.Height/2)), 0, 5);
                    });
                }
                else Assert.Empty(shakePresses);
            }
            if (idleScenario)
            {
                Assert.True(clock.Timestamp >= 26 * 60 * 1000);
                var pulses = input.KeyEdges.Where(edge => edge.Key == 126 && edge.Flags == 0).ToArray();
                if (scenario == "idle-disabled") Assert.Empty(pulses);
                else
                {
                    Assert.InRange(pulses.Length, 12, 14);
                    Assert.Equal(pulses.Length, input.KeyEdges.Count(edge => edge.Key == 126 && edge.Flags == Win32.KEYEVENTF_KEYUP));
                    for (int i = 1; i < pulses.Length; i++) Assert.InRange(pulses[i].At - pulses[i-1].At, 120000, 131000);
                }
            }
        }
    }
}
