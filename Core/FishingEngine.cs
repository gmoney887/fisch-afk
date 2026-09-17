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

    private readonly ScreenCapture _capture = new();
    private readonly ScreenCapture _shakeCapture = new();
    private readonly VisionProcessor _vision = new();
    private readonly FlightRecorder _recorder = new();

    public FlightRecorder Recorder => _recorder;
    public double EstimatedRodPull => _estRodPullAccel;

    public string? LastCastReplicationDir { get; private set; }
    public string? LastAquariumReplicationDir { get; private set; }
    public bool IsStopQueued { get; set; } = false;

    // Feature 1: Session Analytics & Catch Tracking
    public int TotalCatches { get; private set; } = 0;
    public int TotalFails { get; private set; } = 0;
    public int CurrentStreak { get; private set; } = 0;
    private readonly Stopwatch _sessionStopwatch = new();

    public double SessionUptimeSeconds => _sessionStopwatch.Elapsed.TotalSeconds;
    public double CatchesPerHour => (SessionUptimeSeconds > 5) ? (TotalCatches * 3600.0 / SessionUptimeSeconds) : 0.0;
    public double WinRate => (TotalCatches + TotalFails > 0) ? (TotalCatches * 100.0 / (TotalCatches + TotalFails)) : 100.0;

    public void ResetStats()
    {
        TotalCatches = 0;
        TotalFails = 0;
        CurrentStreak = 0;
        _sessionStopwatch.Restart();
    }

    // Feature 2: Anti-AFK Kick Immunity (Roblox 20-minute idle disconnect reset)
    private long _lastAntiAfkTime = 0;
    private int _antiAfkCount = 0;

    // Feature 3: Humanized Timing Jitter (Anti-Macro Detection)
    private static readonly Random _jitterRng = new();

    public int GetJitteredMs(int baseMs, int jitterRangeMs)
    {
        if (!Config.EnableHumanizedJitter || jitterRangeMs <= 0) return baseMs;
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
    }

    private void CheckAntiAfkHeartbeat()
    {
        if (!Config.EnableAntiAfk) return;
        long nowTicks = Stopwatch.GetTimestamp();
        long intervalMs = Math.Max(1, Config.AntiAfkIntervalMinutes) * 60 * 1000;
        if (_lastAntiAfkTime == 0)
        {
            _lastAntiAfkTime = nowTicks;
            return;
        }

        if (GetElapsedMs(_lastAntiAfkTime) >= intervalMs)
        {
            _lastAntiAfkTime = nowTicks;
            _antiAfkCount++;
            // Benign right-click micro-nudge (1 pixel) to reset Roblox's 20-minute idle disconnect timer
            Win32.mouse_event((int)Win32.MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
            Win32.SendRelativeMove(1, 0);
            Thread.Sleep(15);
            Win32.SendRelativeMove(-1, 0);
            Thread.Sleep(10);
            Win32.mouse_event((int)Win32.MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
        }
    }

    // Feature 5: Personal Aquarium Auto-Claim Rewards (Hourly)
    private long _lastAquariumClaimTime = 0;
    private bool _isAquariumClaimPending = false;

    public void CheckAquariumClaimHeartbeat()
    {
        if (!Config.EnableAutoClaimAquarium) return;
        long intervalMs = Math.Max(5, Config.AquariumClaimIntervalMinutes) * 60L * 1000L;
        if (_lastAquariumClaimTime == 0)
        {
            _lastAquariumClaimTime = Stopwatch.GetTimestamp();
            return;
        }

        if (GetElapsedMs(_lastAquariumClaimTime) >= intervalMs)
        {
            _isAquariumClaimPending = true;
        }
    }

    public void TriggerImmediateAquariumClaim()
    {
        _isAquariumClaimPending = true;
    }

    /// <summary>
    /// Executes the Aquarium Claim sequence:
    /// 1. Brings Roblox to foreground.
    /// 2. Clicks the top 'Aquariums' navigation button (u ≈ 0.5303, v ≈ 0.0374).
    /// 3. Pauses ~850ms for modal slide-in animation.
    /// 4. Clicks the 'Claim' button (u ≈ 0.4141, v ≈ 0.5502).
    /// 5. Pauses ~650ms for claim processing.
    /// 6. Clicks the red 'X' close button (u ≈ 0.7227, v ≈ 0.1308).
    /// 7. Pauses ~500ms for modal to close.
    /// 8. Re-equips fishing rod via ReEquipRod() and resets timers.
    /// </summary>
    public bool ExecuteAquariumClaim(bool recordReplication = true)
    {
        IntPtr robloxHwnd = Win32.FindRobloxWindow();
        if (robloxHwnd == IntPtr.Zero || !Win32.GetClientRect(robloxHwnd, out Win32.RECT clientRect) || clientRect.Width <= 0 || clientRect.Height <= 0)
        {
            return false;
        }

        Win32.ForceSetForegroundWindow(robloxHwnd);
        Thread.Sleep(250);

        int clientW = clientRect.Width;
        int clientH = clientRect.Height;

        // Step 1: Click "Aquariums" top navigation item
        // Center-anchored, height-scaled math calibrated for 16:9, 16:10, and 21:9 (+0.0769 clientH)
        int aqClientX = (clientW / 2) + (int)Math.Round(clientH * 0.0769);
        int aqClientY = (int)Math.Round(clientH * 0.0251);
        Win32.POINT aqPt = new Win32.POINT { X = aqClientX, Y = aqClientY };
        if (Win32.ClientToScreen(robloxHwnd, ref aqPt))
        {
            Win32.SendHardwareClick(aqPt.X, aqPt.Y, aqClientX, aqClientY, robloxHwnd);
        }

        // Wait for modal slide-in animation
        Thread.Sleep(900);

        // Step 2: Click "Claim" button (Height-anchored math + CV Auto-Snap)
        int clmBaseX = (clientW / 2) - (int)Math.Round(clientH * 0.184);
        int clmBaseY = (clientH / 2) + (int)Math.Round(clientH * 0.0257);
        using (var snap = _capture.CaptureClientRegion(robloxHwnd, 0, 0, clientW, clientH))
        {
            if (snap != null && !snap.Empty())
            {
                var snapped = _vision.DynamicUISnap(snap, clmBaseX, clmBaseY, VisionProcessor.UIColorType.GreenButton);
                Win32.POINT clmPt = new Win32.POINT { X = snapped.X, Y = snapped.Y };
                if (Win32.ClientToScreen(robloxHwnd, ref clmPt))
                {
                    Win32.SendHardwareClick(clmPt.X, clmPt.Y, snapped.X, snapped.Y, robloxHwnd);
                }
            }
        }

        // Wait for claim processing
        Thread.Sleep(750);

        // Step 3: Click red "X" close button (Height-anchored math + CV Auto-Snap)
        // Red "X" is located at the top-right corner of the whole modal dialog:
        // +0.5602 * clientH horizontally, -0.3795 * clientH vertically
        int xBaseX = (clientW / 2) + (int)Math.Round(clientH * 0.5602);
        int xBaseY = (clientH / 2) - (int)Math.Round(clientH * 0.3795);
        using (var snap2 = _capture.CaptureClientRegion(robloxHwnd, 0, 0, clientW, clientH))
        {
            if (snap2 != null && !snap2.Empty())
            {
                var (found, snapped) = _vision.DynamicUISnapWithStatus(snap2, xBaseX, xBaseY, VisionProcessor.UIColorType.RedCloseButton, 120);
                if (found)
                {
                    Win32.POINT xPt = new Win32.POINT { X = snapped.X, Y = snapped.Y };
                    if (Win32.ClientToScreen(robloxHwnd, ref xPt))
                    {
                        Win32.SendHardwareClick(xPt.X, xPt.Y, snapped.X, snapped.Y, robloxHwnd);
                    }
                }
                else
                {
                    // Safety guard: NEVER click near (2248, 176) which is the "My Aquarium" teleport button!
                    // Fallback to clicking the top navigation "Aquariums" item to toggle the modal closed safely.
                    if (Win32.ClientToScreen(robloxHwnd, ref aqPt))
                    {
                        Win32.SendHardwareClick(aqPt.X, aqPt.Y, aqClientX, aqClientY, robloxHwnd);
                    }
                }
            }
        }

        // Wait for modal to close
        Thread.Sleep(500);

        // Step 4: Ensure cursor is safely back in the game water
        int waterX = (int)Math.Round(clientW * 0.50);
        int waterY = (int)Math.Round(clientH * 0.40);
        Win32.POINT wPt = new Win32.POINT { X = waterX, Y = waterY };
        if (Win32.ClientToScreen(robloxHwnd, ref wPt))
        {
            Win32.SetCursorPos(wPt.X, wPt.Y);
        }

        // Reset tracking
        _lastAquariumClaimTime = Stopwatch.GetTimestamp();
        _isAquariumClaimPending = false;
        Config.LastAquariumClaimUtc = DateTime.UtcNow;

        return true;
    }

    /// <summary>
    /// Executes the full Fisch Equipment Bag crate-opening workflow:
    /// 1. Unequips the fishing rod so clicks never initiate an accidental cast in the 3D world.
    /// 2. Presses 'g' to open up the Equipment / Backpack menu.
    /// 3. Clicks the search bar near top-center and types 'crate' (with Enter) to filter down to crate items.
    private bool IsBagOpen(IntPtr robloxHwnd, int clientW, int clientH)
    {
        try
        {
            // Aspect-ratio independent anchor math for red [X] button
            int cx = (clientW / 2) + (int)Math.Round(clientH * 0.1522);
            int cy = (clientH / 2) + (int)Math.Round(clientH * 0.172);
            using var snap = _shakeCapture.CaptureClientRegion(robloxHwnd, Math.Max(0, cx - 6), Math.Max(0, cy - 6), 13, 13);
            if (snap == null || snap.Empty()) return false;

            int rows = snap.Rows;
            int cols = snap.Cols;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    Vec4b bgra = snap.At<Vec4b>(r, c);
                    // Vibrant Red: R > 160, G < 70, B < 70
                    if (bgra.Item2 > 160 && bgra.Item1 < 70 && bgra.Item0 < 70)
                        return true;
                }
            }
        }
        catch { }
        return false;
    }

    private bool IsCrateDialogOpen(IntPtr robloxHwnd, int clientW, int clientH)
    {
        try
        {
            // Aspect-ratio independent anchor math for [Yes] button
            int cx = (clientW / 2) - (int)Math.Round(clientH * 0.1173);
            int cy = (clientH / 2) + (int)Math.Round(clientH * 0.071);
            using var snap = _shakeCapture.CaptureClientRegion(robloxHwnd, Math.Max(0, cx - 8), Math.Max(0, cy - 8), 17, 17);
            if (snap == null || snap.Empty()) return false;

            int rows = snap.Rows;
            int cols = snap.Cols;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    Vec4b bgra = snap.At<Vec4b>(r, c);
                    // Light green [Yes] text: G > 175 && G > R + 20 && G > B + 20
                    if (bgra.Item1 > 175 && bgra.Item1 > bgra.Item2 + 20 && bgra.Item1 > bgra.Item0 + 20)
                        return true;
                }
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Opens all crates currently in the player's inventory:
    /// 1. Unequips fishing rod on Config.RodSlot so clicks don't cast.
    /// 2. Presses 'g' to open Equipment bag.
    /// 3. Clicks search box and searches for 'crate'.
    /// 4. Iteratively opens Tile 1 (which shifts next crates in as each type is fully unpacked):
    ///    - Clicks Tile 1, clicks outside to trigger dialog.
    ///    - Types 999 into quantity box and clicks [Yes].
    ///    - Dynamically waits for the server animation & dialog dismissal.
    ///    - Reopens bag with 'g' and verifies bag open state.
    /// 5. Automatically finishes when no more crates match or maxTypes is reached.
    /// 6. Safely closes bag if open and re-equips the fishing rod.
    /// </summary>
    public bool ExecuteAutoOpenCrates(int maxCrateTypes = 25, Action<string>? onProgress = null, CancellationToken ct = default)
    {
        IntPtr robloxHwnd = Win32.FindRobloxWindow();
        if (robloxHwnd == IntPtr.Zero || !Win32.GetClientRect(robloxHwnd, out Win32.RECT clientRect) || clientRect.Width <= 0 || clientRect.Height <= 0)
        {
            return false;
        }

        int clientW = clientRect.Width;
        int clientH = clientRect.Height;

        // Focus Roblox window
        Win32.ForceSetForegroundWindow(robloxHwnd);
        Thread.Sleep(250);

        if (ct.IsCancellationRequested) return false;

        // 1. Unequip the fishing rod so clicks don't start casting
        onProgress?.Invoke("📦 Unequipping fishing rod...");
        char rodKey = (!string.IsNullOrEmpty(Config.RodSlot) && Config.RodSlot.Length >= 1) ? Config.RodSlot[0] : '1';
        Win32.SendKeyPress(rodKey); // Toggle unequip tool with hardware scan code
        Thread.Sleep(350);


        if (ct.IsCancellationRequested) return false;

        // 2. Open Equipment list ('g')
        onProgress?.Invoke("🎒 Opening Equipment bag ('G')...");
        if (!IsBagOpen(robloxHwnd, clientW, clientH))
        {
            Win32.SendKeyPress('g');
            int waitBag = 0;
            while (waitBag < 1500 && !IsBagOpen(robloxHwnd, clientW, clientH) && !ct.IsCancellationRequested)
            {
                Thread.Sleep(80);
                waitBag += 80;
            }
            if (!IsBagOpen(robloxHwnd, clientW, clientH))
            {
                Win32.SendKeyPress('g');
                Thread.Sleep(500);
            }
        }


        if (ct.IsCancellationRequested)
        {
            if (IsBagOpen(robloxHwnd, clientW, clientH)) Win32.SendKeyPress('g');
            ReEquipRod();
            return false;
        }

        // 3. Search for 'crate' in Equipment bag
        onProgress?.Invoke("🔍 Searching 'crate' in equipment list...");
        int searchClientX = (clientW / 2) + (int)Math.Round(clientH * 0.0498);
        int searchClientY = (clientH / 2) + (int)Math.Round(clientH * 0.202);
        Win32.POINT sPt = new Win32.POINT { X = searchClientX, Y = searchClientY };
        if (Win32.ClientToScreen(robloxHwnd, ref sPt))
        {
            Win32.SendHardwareClick(sPt.X, sPt.Y, searchClientX, searchClientY, robloxHwnd);
        }
        Thread.Sleep(250);

        // Select all and type 'crate'
        Win32.SelectAllAndClear();
        Thread.Sleep(80);
        Win32.SendKeyString("crate", 45);
        Thread.Sleep(300);


        if (ct.IsCancellationRequested)
        {
            if (IsBagOpen(robloxHwnd, clientW, clientH)) Win32.SendKeyPress('g');
            ReEquipRod();
            return false;
        }

        // 4. Open crates (per quantity available):
        // Aspect-ratio independent anchor math for inventory/dialog coordinates
        int tileX = (clientW / 2) - (int)Math.Round(clientH * 0.1458);
        int tileY = (clientH / 2) + (int)Math.Round(clientH * 0.272);
        int outsideX = (clientW / 2);
        int outsideY = (clientH / 2) - (int)Math.Round(clientH * 0.250);
        int qtyX = (clientW / 2);
        int qtyY = (clientH / 2) + (int)Math.Round(clientH * 0.071);
        int yesBaseX = (clientW / 2) - (int)Math.Round(clientH * 0.1173);
        int yesBaseY = (clientH / 2) + (int)Math.Round(clientH * 0.071);

        int loopLimit = (maxCrateTypes > 0) ? maxCrateTypes : 500;
        int unpackedCount = 0;

        for (int i = 0; i < loopLimit; i++)
        {
            if (ct.IsCancellationRequested) break;

            string targetLabel = (maxCrateTypes > 0) ? $"{i + 1}/{maxCrateTypes}" : $"{i + 1} (All)";
            onProgress?.Invoke($"📦 Unpacking Crate type {targetLabel}...");

            // If after first crate, ensure Equipment bag ('g') is open
            if (i > 0)
            {
                if (!IsBagOpen(robloxHwnd, clientW, clientH))
                {
                    Win32.SendKeyPress('g');
                    int bagWait = 0;
                    while (bagWait < 1500 && !IsBagOpen(robloxHwnd, clientW, clientH) && !ct.IsCancellationRequested)
                    {
                        Thread.Sleep(100);
                        bagWait += 100;
                    }
                    if (!IsBagOpen(robloxHwnd, clientW, clientH))
                    {
                        Win32.SendKeyPress('g');
                        Thread.Sleep(500);
                    }
                }

                if (ct.IsCancellationRequested) break;
            }

            // A. Click on Tile 1 (first crate matching "crate")
            Win32.POINT tPt = new Win32.POINT { X = tileX, Y = tileY };
            if (Win32.ClientToScreen(robloxHwnd, ref tPt))
            {
                Win32.SendHardwareClick(tPt.X, tPt.Y, tileX, tileY, robloxHwnd);
            }
            Thread.Sleep(300);


            if (ct.IsCancellationRequested) break;

            // B. Click outside of equipment window to trigger action and open dialog
            Win32.POINT outPt = new Win32.POINT { X = outsideX, Y = outsideY };
            if (Win32.ClientToScreen(robloxHwnd, ref outPt))
            {
                Win32.SendHardwareClick(outPt.X, outPt.Y, outsideX, outsideY, robloxHwnd);
            }

            // Dynamically wait up to 1200ms for Open Crates dialog to appear
            int diagWait = 0;
            while (diagWait < 1200 && !IsCrateDialogOpen(robloxHwnd, clientW, clientH) && !ct.IsCancellationRequested)
            {
                Thread.Sleep(80);
                diagWait += 80;
            }


            // If dialog did not open, no crates are left in inventory matching "crate"!
            if (!IsCrateDialogOpen(robloxHwnd, clientW, clientH))
            {
                onProgress?.Invoke($"🎉 All crates unpacked! Finished {unpackedCount} type(s).");
                break;
            }

            if (ct.IsCancellationRequested) break;

            // C. Set max number of crates to open (click quantity box & type 999)
            Win32.POINT qPt = new Win32.POINT { X = qtyX, Y = qtyY };
            if (Win32.ClientToScreen(robloxHwnd, ref qPt))
            {
                Win32.SendHardwareClick(qPt.X, qPt.Y, qtyX, qtyY, robloxHwnd);
            }
            Thread.Sleep(150);
            Win32.SelectAllAndClear();
            Thread.Sleep(80);
            Win32.SendKeyString("999", 35);
            Thread.Sleep(200);


            if (ct.IsCancellationRequested) break;

            // D. Click [Yes] to open all crates of this type (with Auto-Snap)
            using (var snap = _capture.CaptureClientRegion(robloxHwnd, 0, 0, clientW, clientH))
            {
                if (snap != null && !snap.Empty())
                {
                    var snapped = _vision.DynamicUISnap(snap, yesBaseX, yesBaseY, VisionProcessor.UIColorType.GreenButton);
                    Win32.POINT yPt = new Win32.POINT { X = snapped.X, Y = snapped.Y };
                    if (Win32.ClientToScreen(robloxHwnd, ref yPt))
                    {
                        Win32.SendHardwareClick(yPt.X, yPt.Y, snapped.X, snapped.Y, robloxHwnd);
                    }
                }
            }

            unpackedCount++;

            // Dynamic wait: wait until the Open Crates dialog dismisses (up to 8 seconds for bulk animations)
            int dismissWait = 0;
            while (dismissWait < 8000 && IsCrateDialogOpen(robloxHwnd, clientW, clientH) && !ct.IsCancellationRequested)
            {
                Thread.Sleep(150);
                dismissWait += 150;
            }

            // Grace period for server sync & bag dismissal
            Thread.Sleep(600);
        }

        // 5. If equipment bag is still open, close it cleanly
        if (IsBagOpen(robloxHwnd, clientW, clientH))
        {
            Win32.SendKeyPress('g');
            Thread.Sleep(300);
        }

        // 6. Safely re-equip fishing rod on configured rod slot
        onProgress?.Invoke("🎣 Re-equipping fishing rod...");
        ReEquipRod();
        Thread.Sleep(400);


        onProgress?.Invoke($"✅ Crate Routine Complete! Unpacked {unpackedCount} crate types.");
        return true;
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

    private long _lastDebugFrameTick = 0;
    private long _lastShakeClickTime = 0;
    private int _shakeMemoryX = 0;
    private int _shakeMemoryY = 0;
    private int _shakeRepeatCounter = 0;

    public FishingEngine(Settings settings)
    {
        Config = settings;
        _recorder.MaxRecordingsToKeep = Config.MaxRecordingsToKeep;
        ApplyRodProfile(Config.RodProfile);
    }

    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        Win32.timeBeginPeriod(1);
        SessionLogger.Instance.LogState(CurrentState, MacroState.Casting, "User Started Macro");
        CurrentState = MacroState.Casting;
        _stateStartTime = Stopwatch.GetTimestamp();
        _lastAntiAfkTime = Stopwatch.GetTimestamp();
        _sessionStopwatch.Start();

        _workerTask = Task.Run(() => WorkerLoop(_cts.Token));
    }

    public void Stop()
    {
        if (!IsRunning) return;

        _cts?.Cancel();
        try { _workerTask?.Wait(500); } catch { }

        SetMouseDown(false);
        Win32.timeEndPeriod(1);
        _sessionStopwatch.Stop();
        SessionLogger.Instance.LogState(CurrentState, MacroState.Stopped, "User Stopped Macro");
        CurrentState = MacroState.Stopped;

        var telem = new TelemetryData
        {
            State = MacroState.Stopped,
            Action = "Stopped"
        };
        PopulateTelemetryStats(telem);
        OnTelemetry?.Invoke(telem);
    }

    public void ClickHotbarSlot(IntPtr robloxHwnd, int clientW, int clientH, int slotNum)
    {
        slotNum = Math.Clamp(slotNum, 1, 9);
        double offsetRatio = (slotNum - 5) * 0.05607;
        int slotClientX = (clientW / 2) + (int)Math.Round(clientH * offsetRatio);
        int slotClientY = (clientH / 2) + (int)Math.Round(clientH * 0.4509);
        if (Win32.SanitizeGameCoordinate(robloxHwnd, slotClientX, slotClientY, out int safeX, out int safeY, out int sX, out int sY))
        {
            SessionLogger.Instance.Log("ROD", $"ClickHotbarSlot {slotNum}: Client=({safeX}, {safeY}) -> Screen=({sX}, {sY})");
            Win32.SendHardwareClick(sX, sY, safeX, safeY, robloxHwnd);
        }
    }

    public bool IsRodEquipped(IntPtr robloxHwnd, int clientW, int clientH)
    {
        try
        {
            int slotNum = 1;
            if (!string.IsNullOrEmpty(Config.RodSlot) && char.IsDigit(Config.RodSlot[0]))
            {
                slotNum = Math.Clamp(Config.RodSlot[0] - '0', 1, 9);
            }

            // Aspect-ratio independent anchor math for hotbar slots (Slot 5 is dead center)
            double offsetRatio = (slotNum - 5) * 0.05607;
            int slotClientX = (clientW / 2) + (int)Math.Round(clientH * offsetRatio);
            int slotClientY = (clientH / 2) + (int)Math.Round(clientH * 0.4509);

            // Sample the upper interior of the slot box just below the top divider
            int boxHalfW = (int)Math.Round(clientH * 0.015);
            int topBorderY = slotClientY - (int)Math.Round(clientH * 0.033);
            int sampleH = (int)Math.Max(4, Math.Round(clientH * 0.018));

            using var snap = _shakeCapture.CaptureClientRegion(robloxHwnd, Math.Max(0, slotClientX - boxHalfW), Math.Max(0, topBorderY + 1), boxHalfW * 2, sampleH);
            if (snap == null || snap.Empty()) return true; // Safe fallback: assume equipped so we never toggle it off

            int rows = snap.Rows;
            int cols = snap.Cols;
            int selectionCount = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    Vec4b bgra = snap.At<Vec4b>(r, c);
                    // Blue selection: Item0(B) > 130 && Item0(B) > Item2(R) + 25
                    // White selection: Item0(B) > 175 && Item1(G) > 175 && Item2(R) > 175
                    bool isBlue = bgra.Item0 > 130 && bgra.Item0 > bgra.Item2 + 25;
                    bool isWhite = bgra.Item0 > 175 && bgra.Item1 > 175 && bgra.Item2 > 175;
                    if (isBlue || isWhite)
                    {
                        selectionCount++;
                    }
                }
            }

            return selectionCount >= 12;
        }
        catch { }
        return true;
    }

    public void EnsureRodEquipped(IntPtr robloxHwnd, int clientW, int clientH)
    {
        if (IsRodEquipped(robloxHwnd, clientW, clientH))
        {
            SessionLogger.Instance.Log("ROD", "EnsureRodEquipped: Rod is ALREADY in hand. No action taken.");
            return;
        }

        SessionLogger.Instance.Log("ROD", "EnsureRodEquipped: Rod is NOT in hand. Equipping rod now...");

        char rodKey = (!string.IsNullOrEmpty(Config.RodSlot) && char.IsDigit(Config.RodSlot[0])) ? Config.RodSlot[0] : '1';
        int slotNum = Math.Clamp(rodKey - '0', 1, 9);

        // Send hotkey once
        Win32.SendKeyPress(rodKey);
        Thread.Sleep(150);

        if (IsRodEquipped(robloxHwnd, clientW, clientH))
        {
            SessionLogger.Instance.Log("ROD", "EnsureRodEquipped: Rod successfully equipped via keypress.");
            return;
        }

        // If hotkey was ignored (e.g. chat focus active), click the slot directly
        SessionLogger.Instance.Log("ROD", $"EnsureRodEquipped: Keypress did not equip. Clicking hotbar slot {slotNum}...");
        ClickHotbarSlot(robloxHwnd, clientW, clientH, slotNum);
        Thread.Sleep(150);

        // Return cursor to safe water
        int waterClientX = clientW / 2;
        int waterClientY = (int)Math.Round(clientH * 0.38);
        if (Win32.SafeClientToScreen(robloxHwnd, waterClientX, waterClientY, out int sx, out int sy))
        {
            Win32.SetCursorPos(sx, sy);
        }
    }

    public void ReEquipRod(bool clickSlot = false)
    {
        IntPtr robloxHwnd = Win32.FindRobloxWindow();
        if (robloxHwnd == IntPtr.Zero || !Win32.GetClientRect(robloxHwnd, out Win32.RECT clientRect) || clientRect.Width <= 0 || clientRect.Height <= 0)
        {
            return;
        }

        if (Win32.GetForegroundWindow() != robloxHwnd)
        {
            Win32.ForceSetForegroundWindow(robloxHwnd);
            Thread.Sleep(200);
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
            EnsureRodEquipped(robloxHwnd, winW, winH);
        }
    }

    public void ExecuteTestCast()
    {
        IntPtr robloxHwnd = Win32.FindRobloxWindow();
        if (robloxHwnd == IntPtr.Zero || !Win32.GetClientRect(robloxHwnd, out Win32.RECT clientRect) || clientRect.Width <= 0 || clientRect.Height <= 0)
        {
            return;
        }

        ReEquipRod(clickSlot: true);

        SetMouseDown(true);
        Thread.Sleep(Config.CastHoldMs);
        SetMouseDown(false);
    }

    /// <summary>
    /// Executes a single vision-calibrated test cast, tracks the rising power bar in real-time,
    /// measures the exact millisecond duration to hit 100% peak, updates Config.CastHoldMs,
    /// and returns the calibrated milliseconds (or -1 if window/bar was not detected).
    /// </summary>
    public int ExecuteAutoTuneCast(bool recordReplication = true)
    {
        IntPtr robloxHwnd = Win32.FindRobloxWindow();
        if (robloxHwnd == IntPtr.Zero || !Win32.GetClientRect(robloxHwnd, out Win32.RECT clientRect) || clientRect.Width <= 0 || clientRect.Height <= 0)
        {
            return -1;
        }

        string? recDir = null;
        if (recordReplication)
        {
            recDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recordings", "cast_replications", $"run_{DateTime.Now:yyyyMMdd_HHmmss}");
            try { Directory.CreateDirectory(recDir); } catch { }
            LastCastReplicationDir = recDir;
        }

        ReEquipRod(clickSlot: true);

        int winW = clientRect.Width;
        int winH = clientRect.Height;

        var frameBuffer = new List<(long elapsedMs, string stage, double fill, Mat frame)>();

        SetMouseDown(true);
        long startTicks = Stopwatch.GetTimestamp();
        int calibratedMs = -1;
        int maxHoldDuration = Math.Max(1150, Config.CastHoldMs + 250);

        int roiX = (int)(winW * 0.50);
        int roiY = (int)(winH * 0.30);
        int roiW = (int)(winW * 0.12);
        int roiH = (int)(winH * 0.45);

        double lastFill = -1.0;
        double lastFillElapsed = 0.0;
        double fillRate = 0.135;
        double leadMs = Config.CastPredictiveLeadMs > 0 ? Config.CastPredictiveLeadMs : 25.0;

        while (GetElapsedMs(startTicks) < maxHoldDuration && (_cts == null || !_cts.Token.IsCancellationRequested))
        {
            double elapsed = GetElapsedMs(startTicks);
            var frame = _shakeCapture.CaptureClientRegion(robloxHwnd, roiX, roiY, roiW, roiH);
            if (frame != null && !frame.Empty())
            {
                var castRes = _vision.DetectCastBarROI(frame, winH, roiX, roiY, Config.SelectedTheme, Config.ShowVisionPreview);
                if (recordReplication)
                {
                    frameBuffer.Add(((long)elapsed, "HOLD", castRes.Found ? castRes.FillPercent : -1, frame.Clone()));
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

                    if (Config.ShowVisionPreview && castRes.AnnotatedFrame != null)
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

            Thread.Sleep(2);
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
            while (GetElapsedMs(startTicks) < postReleaseTarget && (_cts == null || !_cts.Token.IsCancellationRequested))
            {
                double elapsed = GetElapsedMs(startTicks);
                var frame = _shakeCapture.CaptureClientRegion(robloxHwnd, 0, 0, winW, winH);
                if (frame != null && !frame.Empty())
                {
                    var castRes = _vision.DetectCastBar(frame, Config.SelectedTheme, false);
                    frameBuffer.Add(((long)elapsed, "RELEASED", castRes.Found ? castRes.FillPercent : -1, frame.Clone()));
                    frame.Dispose();
                }
                Thread.Sleep(20);
            }

            // Write all buffered frames out to disk
            if (recDir != null && frameBuffer.Count > 0)
            {
                try
                {
                    var manifestLines = new List<string>
                    {
                        $"Cast Auto-Tune Diagnostic Recording",
                        $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                        $"Resolution: {winW}x{winH}",
                        $"Release Elapsed: {releaseMs}ms",
                        $"Calibrated Timing: {(calibratedMs > 0 ? calibratedMs + "ms" : "UNRESOLVED")}",
                        $"Total Frames: {frameBuffer.Count}",
                        "------------------------------------------------"
                    };

                    for (int i = 0; i < frameBuffer.Count; i++)
                    {
                        var item = frameBuffer[i];
                        string fn = $"frame_{i:D3}_{item.elapsedMs}ms_{item.stage}.png";
                        Cv2.ImWrite(Path.Combine(recDir, fn), item.frame);
                        manifestLines.Add($"[{i:D3}] +{item.elapsedMs,4}ms | {item.stage,-8} | Fill: {item.fill,5:F1}% | {fn}");
                        item.frame.Dispose();
                    }
                    frameBuffer.Clear();

                    File.WriteAllLines(Path.Combine(recDir, "manifest.txt"), manifestLines);
                }
                catch { }
            }
        }

        return calibratedMs;
    }

    private void SetMouseDown(bool down)
    {
        if (_isMouseDown != down)
        {
            _isMouseDown = down;
            SessionLogger.Instance.LogInput(down ? "MouseDown" : "MouseUp", -1, -1);
            Win32.mouse_event(down ? Win32.MOUSEEVENTF_LEFTDOWN : Win32.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        }
    }

    private void Transition(MacroState newState, string reason = "")
    {
        MacroState oldState = CurrentState;
        if (oldState == MacroState.Reeling && newState != MacroState.Reeling)
        {
            _recorder.StopSession(newState.ToString());
        }

        double elapsed = (_stateStartTime > 0) ? GetElapsedMs(_stateStartTime) : 0;
        SessionLogger.Instance.LogState(oldState, newState, $"{reason} (Elapsed in {oldState}: {elapsed:F0}ms)");

        SetMouseDown(false);
        CurrentState = newState;
        _stateStartTime = Stopwatch.GetTimestamp();
        _lastBarSeenTime = Stopwatch.GetTimestamp();
        _lastFishSeenTime = Stopwatch.GetTimestamp();
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
        return (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
    }

    private void EnsureCursorInGameView(IntPtr hwnd, Win32.RECT clientRect)
    {
        if (Win32.GetCursorPos(out Win32.POINT mousePt))
        {
            Win32.POINT clientTopLeft = new Win32.POINT { X = 0, Y = 0 };
            if (Win32.ClientToScreen(hwnd, ref clientTopLeft))
            {
                int screenLeft = clientTopLeft.X;
                int screenTop = clientTopLeft.Y;
                int screenRight = screenLeft + clientRect.Width;
                int screenBottom = screenTop + clientRect.Height;

                IntPtr winUnderCursor = Win32.WindowFromPoint(mousePt);
                Win32.GetWindowThreadProcessId(winUnderCursor, out uint curPid);
                uint macroPid = (uint)Environment.ProcessId;
                bool isCoveredByMacro = (curPid == macroPid);

                int hotbarMargin = (int)Math.Max(clientRect.Height * 0.14, 85);
                bool isInsideRoblox = (mousePt.X >= screenLeft + 60 && mousePt.X <= screenRight - 60 &&
                                       mousePt.Y >= screenTop + 60 && mousePt.Y <= screenBottom - hotbarMargin);

                if (isInsideRoblox && !isCoveredByMacro)
                {
                    // Cursor is already safely inside the open Roblox water area. Do not move it!
                    return;
                }

                // If cursor is outside or occluded by macro, target upper-center area of Roblox
                int safeX = screenLeft + (clientRect.Width / 2);
                int safeY = screenTop + (int)(clientRect.Height * 0.38);

                IntPtr winAtTarget = Win32.WindowFromPoint(new Win32.POINT { X = safeX, Y = safeY });
                Win32.GetWindowThreadProcessId(winAtTarget, out uint targetPid);
                if (targetPid == macroPid)
                {
                    safeX = screenLeft + Math.Clamp(clientRect.Width / 4, 80, 400);
                }

                Win32.SetCursorPos(safeX, safeY);
            }
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
                loopSw.Restart();
                long nowTicks = Stopwatch.GetTimestamp();

            IntPtr robloxHwnd = Win32.FindRobloxWindow();
            if (robloxHwnd == IntPtr.Zero || !Win32.GetClientRect(robloxHwnd, out Win32.RECT clientRect) || clientRect.Width <= 0 || clientRect.Height <= 0)
            {
                var telem = new TelemetryData
                {
                    State = CurrentState,
                    Action = "Waiting for Roblox window..."
                };
                PopulateTelemetryStats(telem);
                OnTelemetry?.Invoke(telem);
                Thread.Sleep(200);
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
            int trackY2 = (int)(winH * 0.94);
            int trackW = trackX2 - trackX1;
            int trackH = trackY2 - trackY1;

            // ==============================================================
            // STATE 1: CASTING
            // ==============================================================
            if (CurrentState == MacroState.Casting)
            {
                // Capture initial game preview frame so camera monitor is instantly live
                Mat? castFrame = null;
                if (Config.ShowVisionPreview)
                {
                    castFrame = _shakeCapture.CaptureClientRegion(robloxHwnd, 0, 0, winW, winH);
                    if (castFrame != null && !castFrame.Empty())
                    {
                        Cv2.PutText(castFrame, "CASTING ROD...", new Point(14, 24),
                            HersheyFonts.HersheySimplex, 0.45, Scalar.FromRgb(0, 229, 255), 1);
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
                if (Win32.GetForegroundWindow() != robloxHwnd)
                {
                    Win32.SetForegroundWindow(robloxHwnd);
                    Thread.Sleep(150);
                }

                // 1. Ensure the fishing rod is strictly equipped in hand before casting!
                EnsureRodEquipped(robloxHwnd, winW, winH);
                EnsureCursorInGameView(robloxHwnd, clientRect);

                bool barEverFound = false;

                if (Config.EnableDynamicCastRelease)
                {
                    SetMouseDown(true);
                    long castStartTicks = Stopwatch.GetTimestamp();
                    bool dynamicReleased = false;
                    int maxTimeoutMs = Math.Max(1200, Config.CastHoldMs + 250);

                    // Precompute ROI coordinates for ultra-fast BitBlt capture (~1-2ms instead of ~50ms full-window)
                    // Centered around player avatar (35% to 65% horizontally, 25% to 75% vertically - works on 16:9 and 21:9)
                    int roiX = (int)(winW * 0.35);
                    int roiY = (int)(winH * 0.25);
                    int roiW = (int)(winW * 0.30);
                    int roiH = (int)(winH * 0.50);
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
                                var castResult = _vision.DetectCastBarROI(castRoiFrame, winH, roiX, roiY, Config.SelectedTheme, Config.ShowVisionPreview);
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

                                    if (Config.ShowVisionPreview && castResult.AnnotatedFrame != null)
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

                        Thread.Sleep(2);
                    }
                    SetMouseDown(false);
                }
                else
                {
                    SetMouseDown(true);
                    Thread.Sleep(GetJitteredMs(Config.CastHoldMs, 8));
                    SetMouseDown(false);
                }

                // Cast has finished holding and was released with the verified rod in hand.
                // The bobber is in flight/water. Proceed directly to Luring!
                SessionLogger.Instance.Log("CAST", $"Cast hold completed ({GetElapsedMs(_stateStartTime):F0}ms, barEverFound={barEverFound}). Bobber is in water. Transitioning to Luring.");
                Thread.Sleep(GetJitteredMs(Config.PostCastDelayMs, 40));
                Transition(MacroState.Luring, "Cast dispatched");
                continue;
            }

            // Only generate debug annotated frame at ~30 FPS to save CPU and RAM
            bool shouldGenerateDebug = (GetElapsedMs(_lastDebugFrameTick) >= 33.0);
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

                if (luringDetect.BarFound)
                {
                    _luringConfirmCount++;
                    if (_luringConfirmCount >= 2)
                    {
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

                // Timeout check: re-cast if no bite within LureTimeoutMs
                if (GetElapsedMs(_stateStartTime) > Config.LureTimeoutMs)
                {
                    Transition(MacroState.Casting, $"Lure timeout ({Config.LureTimeoutMs}ms reached)");
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
                    var shakeResult = _vision.DetectShakeIcon(fullFrame, 0, 0, scaleFactor, Config.ShowVisionPreview);
                    luringDebugFrame = shakeResult.AnnotatedFrame;

                    if (shakeActive && GetElapsedMs(_lastShakeClickTime) >= GetJitteredMs(Config.ShakeClickIntervalMs, 4))
                    {
                        _lastShakeClickTime = nowTicks;

                        Win32.ForceSetForegroundWindow(robloxHwnd);

                        if (Config.ShakeMode == "Navigation")
                        {
                            // Roblox UI Navigation: Toggle UI focus (Backslash '\' VK_OEM_5 = 0xDC) and activate (Enter VK_RETURN = 0x0D)
                            Win32.keybd_event(0xDC, 0, 0, 0); // Backslash Down
                            Thread.Sleep(10);
                            Win32.keybd_event(0xDC, 0, Win32.KEYEVENTF_KEYUP, 0); // Backslash Up
                            Thread.Sleep(15);
                            Win32.keybd_event(0x0D, 0, 0, 0); // Enter Down
                            Thread.Sleep(10);
                            Win32.keybd_event(0x0D, 0, Win32.KEYEVENTF_KEYUP, 0); // Enter Up
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
                                int safeClickX = Math.Clamp(clickClientX, 40, winW - 40);
                                int safeClickY = Math.Clamp(clickClientY, 40, winH - 40);

                                // Convert client coords to physical screen coordinates
                                Win32.POINT screenPt = new Win32.POINT { X = safeClickX, Y = safeClickY };
                                if (Win32.ClientToScreen(robloxHwnd, ref screenPt))
                                {
                                    IntPtr windowAtPoint = Win32.WindowFromPoint(screenPt);
                                    Win32.GetWindowThreadProcessId(windowAtPoint, out uint ptPid);
                                    uint macroPid = (uint)Environment.ProcessId;
                                    bool isOccludedByMacro = (ptPid == macroPid);

                                    if (!isOccludedByMacro)
                                    {
                                        // Execute high-reliability hardware click (DirectInput, RawInput, SendInput, PostMessage)
                                        Win32.SendHardwareClick(screenPt.X, screenPt.Y, safeClickX, safeClickY, robloxHwnd);
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
                Thread.Sleep(25);
                continue;
            }

            // Capture track crop directly from the Desktop DWM frame for REELING
            visionSw.Restart();
            using Mat? crop = _capture.CaptureClientRegion(robloxHwnd, trackX1, trackY1, trackW, trackH);
            DetectionResult detect = (crop != null)
                ? _vision.ProcessTrack(crop, trackX1, trackY1, scaleFactor, Config.SelectedTheme, shouldGenerateDebug)
                : new DetectionResult();
            visionSw.Stop();
            // ==============================================================
            // STATE 3: REELING (ACTIVE COMPUTER VISION PURSUIT)
            // ==============================================================
            if (CurrentState == MacroState.Reeling)
            {
                double dtSec = (_lastTickTime > 0) ? (GetElapsedMs(_lastTickTime) / 1000.0) : 0.01;
                if (dtSec <= 0 || dtSec > 0.1) dtSec = 0.01;
                _lastTickTime = nowTicks;

                // Ensure Roblox is foreground
                if (Win32.GetForegroundWindow() != robloxHwnd)
                {
                    Win32.SetForegroundWindow(robloxHwnd);
                }

                // Start recorder session if enabled and not already active
                if (Config.EnableRecording && !_recorder.IsRecording)
                {
                    _recorder.StartSession(trackW, trackH);
                }

                if (detect.BarFound)
                {
                    _lastBarSeenTime = nowTicks;

                    // Bar velocity smoothing (pixels per second)
                    if (_prevBarCenter > 0 && dtSec > 0)
                    {
                        double rawVel = (detect.BarCenter - _prevBarCenter) / dtSec;
                        rawVel = Math.Clamp(rawVel, -2500.0, 2500.0);
                        _barVelocity = (_barVelocity * 0.4) + (rawVel * 0.6);

                        // Online Adaptive Rod Dynamic Calibration:
                        // Measures actual rod pull acceleration during MouseDown to auto-tune to any rod
                        if (_isMouseDown && _barVelocity > _prevBarVelocity && detect.BarCenter < trackX2 - 80 && dtSec >= 0.01)
                        {
                            double measuredAccel = (_barVelocity - _prevBarVelocity) / dtSec;
                            if (measuredAccel >= 300.0 && measuredAccel <= 3000.0)
                            {
                                _estRodPullAccel = (_estRodPullAccel * 0.90) + (measuredAccel * 0.10);
                                _estRodPullAccel = Math.Clamp(_estRodPullAccel, 400.0, 2500.0);
                            }
                        }
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

                    if (jumpDist > 175.0)
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
                            rawFVel = Math.Clamp(rawFVel, -2500.0, 2500.0);
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
                        double aFall = _gravityFallAccel;
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
                        _lastPulseTime = Stopwatch.GetTimestamp();
                        _pulseDuration = _pulseState ? Config.HoverDownMs : Config.HoverUpMs;
                    }
                    SetMouseDown(_pulseState);
                }

                // Check lifecycle: terminate reeling if bar disappeared > 1000ms
                if (GetElapsedMs(_lastBarSeenTime) > 450 || GetElapsedMs(_stateStartTime) > Config.ReelTimeoutMs)
                {
                    if (GetElapsedMs(_stateStartTime) > Config.ReelTimeoutMs)
                    {
                        TotalFails++;
                        CurrentStreak = 0;
                    }
                    else if (GetElapsedMs(_stateStartTime) >= 1200) // Minigame lasted at least 1.2s and concluded successfully
                    {
                        TotalCatches++;
                        CurrentStreak++;
                        _catchesSinceLastCrateOpen++;
                    }

                    Transition(MacroState.PostCatch, "Minigame ended (Bar disappeared)");
                    detect.AnnotatedFrame?.Dispose();
                    continue;
                }

                // Render dynamic Action & Mouse HUD directly on the live camera frame
                if (detect.AnnotatedFrame != null && !detect.AnnotatedFrame.Empty())
                {
                    int fH = detect.AnnotatedFrame.Height;
                    int fW = detect.AnnotatedFrame.Width;
                    double errVal = hasFishTarget ? (targetFishX - detect.BarCenter) : 0;

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
                    Error = hasFishTarget ? (targetFishX - detect.BarCenter) : 0,
                    BarVelocity = _barVelocity,
                    FishVelocity = _fishVelocity,
                    RodPullAccel = _estRodPullAccel,
                    LoopLatencyMs = loopSw.Elapsed.TotalMilliseconds,
                    VisionLatencyMs = visionSw.Elapsed.TotalMilliseconds,
                    IsMouseDown = _isMouseDown,
                    AnnotatedFrame = detect.AnnotatedFrame
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
                if (IsStopQueued)
                {
                    IsStopQueued = false;
                    Stop();
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

                    ExecuteAquariumClaim();
                    Thread.Sleep(400);
                }

                if (Config.EnableAutoOpenCrates && _catchesSinceLastCrateOpen >= Config.CrateIntervalCatches)
                {
                    var crateTelem = new TelemetryData
                    {
                        State = MacroState.PostCatch,
                        Action = "📦 Auto-Opening Caught Crates..."
                    };
                    PopulateTelemetryStats(crateTelem);
                    OnTelemetry?.Invoke(crateTelem);

                    ExecuteAutoOpenCrates(Config.CrateMaxTypes, (msg) =>
                    {
                        var t = new TelemetryData { State = MacroState.PostCatch, Action = msg };
                        PopulateTelemetryStats(t);
                        OnTelemetry?.Invoke(t);
                    }, ct);

                    _catchesSinceLastCrateOpen = 0;
                    Thread.Sleep(400);
                }

                var telem = new TelemetryData
                {
                    State = MacroState.PostCatch,
                    Action = "Catch Completed! Resting..."
                };
                PopulateTelemetryStats(telem);
                OnTelemetry?.Invoke(telem);

                if (GetElapsedMs(_stateStartTime) > GetJitteredMs(Config.PostCatchDelayMs, 60))
                {
                    Transition(MacroState.Casting, "PostCatch delay elapsed");
                    continue;
                }
            }

            // Sleep remaining time to maintain ~100Hz (10ms tick rate)
            int elapsedLoop = (int)loopSw.ElapsedMilliseconds;
            int sleepTime = Math.Max(1, 10 - elapsedLoop);
            Thread.Sleep(sleepTime);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SessionLogger.Instance.LogError("WorkerLoop Error", ex);
                try
                {
                    string errPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine_error.log");
                    System.IO.File.AppendAllText(errPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [WorkerLoop Error] {ex}\n");
                }
                catch { }

                // Safe recovery: ensure mouse is released, wait briefly, and resume into Casting
                SetMouseDown(false);
                Thread.Sleep(500);
                if (!ct.IsCancellationRequested)
                {
                    Transition(MacroState.Casting, "Error recovery");
                }
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _recorder.Dispose();
        _shakeCapture.Dispose();
        _capture.Dispose();
    }
}
