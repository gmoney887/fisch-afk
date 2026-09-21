using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using FischMacroCS.Capture;
using FischMacroCS.Native;
using FischMacroCS.Vision;

namespace FischMacroCS.Core;

public enum DiagnosticStatus
{
    Pending,
    Running,
    Pass,
    Warning,
    Fail
}

public class DiagnosticStep
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DiagnosticStatus Status { get; set; } = DiagnosticStatus.Pending;
    public string Details { get; set; } = "";
    public string Metric { get; set; } = "";
    public double DurationMs { get; set; }
}

public class PreFlightReport
{
    public bool OverallPass { get; set; }
    public bool ReconnectAvailable { get; set; }
    public bool ContinueAvailable { get; set; }
    public bool DeathDetected { get; set; }
    public string Summary { get; set; } = "";
    public List<DiagnosticStep> Steps { get; set; } = new();
    public Mat? AnnotatedSnapshot { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public double TotalDurationMs { get; set; }
}

public class PreFlightDiagnostic : IDisposable
{
    private readonly Settings _config;
    private readonly VisionProcessor _vision;
    private readonly IFrameSource _capture;
    private readonly bool _ownsCapture;
    private readonly GameDesktop _desktop;
    private readonly IInputSink? _hardware;

    public PreFlightDiagnostic(Settings config, VisionProcessor? vision = null, IFrameSource? capture = null,
        GameDesktop? desktop = null, IInputSink? hardware = null)
    {
        _config = config;
        _vision = vision ?? new VisionProcessor();
        _desktop = desktop ?? new GameDesktop();
        _hardware = hardware;
        if (capture != null)
        {
            _capture = capture;
            _ownsCapture = false;
        }
        else
        {
            _capture = new ScreenCapture();
            _ownsCapture = true;
        }
    }

    public async Task<PreFlightReport> RunDiagnosticAsync(Action<DiagnosticStep>? onStepUpdate = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var swTotal = Stopwatch.StartNew();
        var report = new PreFlightReport();

        var stepWindow = new DiagnosticStep { Id = "window", Name = "Roblox Window & Resolution", Status = DiagnosticStatus.Pending };
        var stepCapture = new DiagnosticStep { Id = "capture", Name = "Screen Capture Latency Benchmark", Status = DiagnosticStatus.Pending };
        var stepHotbar = new DiagnosticStep { Id = "hotbar", Name = "Dynamic Hotbar & Slot Discovery", Status = DiagnosticStatus.Pending };
        var stepToggle = new DiagnosticStep { Id = "tool_toggle", Name = "Live Tool Toggle Probe", Status = DiagnosticStatus.Pending };
        var stepSafety = new DiagnosticStep { Id = "coordinates", Name = "Coordinate Boundary & Safety Audit", Status = DiagnosticStatus.Pending };

        report.Steps.Add(stepWindow);
        report.Steps.Add(stepCapture);
        report.Steps.Add(stepHotbar);
        report.Steps.Add(stepToggle);
        report.Steps.Add(stepSafety);

        char rodKey = (!string.IsNullOrEmpty(_config.RodSlot) && char.IsDigit(_config.RodSlot[0])) ? _config.RodSlot[0] : '1';
        int slotNum = Math.Clamp(rodKey - '0', 1, 9);

        // =========================================================================
        // STEP 1: Roblox Window & Resolution Inspection
        // =========================================================================
        stepWindow.Status = DiagnosticStatus.Running;
        onStepUpdate?.Invoke(stepWindow);
        var swStep = Stopwatch.StartNew();

        ct.ThrowIfCancellationRequested();
        IntPtr robloxHwnd = _desktop.FindRobloxWindow();
        if (robloxHwnd == IntPtr.Zero || _desktop.IsIconic(robloxHwnd) || !_desktop.GetClientRect(robloxHwnd, out Win32.RECT clientRect) || clientRect.Width < 300 || clientRect.Height < 200)
        {
            stepWindow.Status = DiagnosticStatus.Fail;
            stepWindow.Details = "Roblox window ('RobloxPlayerBeta') was not found or is minimized. Please open Fisch and ensure window is visible.";
            stepWindow.Metric = "Window Missing";
            stepWindow.DurationMs = swStep.Elapsed.TotalMilliseconds;
            onStepUpdate?.Invoke(stepWindow);

            report.OverallPass = false;
            report.Summary = "Roblox client window was not detected. Start Roblox Fisch before running diagnostic.";
            report.TotalDurationMs = swTotal.Elapsed.TotalMilliseconds;
            return report;
        }

        int clientW = clientRect.Width;
        int clientH = clientRect.Height;
        double aspect = (double)clientW / clientH;
        string aspectDesc = aspect >= 3.2 ? "32:9 Super Ultrawide" : aspect >= 2.1 ? "21:9 Ultrawide" : aspect >= 1.7 ? "16:9 Widescreen" : aspect >= 1.55 ? "16:10" : $"{aspect:F2}:1";

        stepWindow.Status = DiagnosticStatus.Pass;
        stepWindow.Metric = $"{clientW}x{clientH} ({aspectDesc})";
        stepWindow.Details = $"Attached to Roblox client at {clientW}x{clientH} ({aspectDesc}). Window is active.";
        stepWindow.DurationMs = swStep.Elapsed.TotalMilliseconds;
        onStepUpdate?.Invoke(stepWindow);

        await Task.Delay(40, ct);

        // =========================================================================
        // STEP 2: Screen Capture Latency Benchmark
        // =========================================================================
        stepCapture.Status = DiagnosticStatus.Running;
        onStepUpdate?.Invoke(stepCapture);
        swStep.Restart();

        ct.ThrowIfCancellationRequested();
        _desktop.ForceSetForegroundWindow(robloxHwnd);
        await Task.Delay(80, ct);

        uint initialDpi = _desktop.GetDpiForWindow(robloxHwnd);
        void ValidateContext()
        {
            ct.ThrowIfCancellationRequested();
            if (_desktop.GetForegroundWindow() != robloxHwnd || _desktop.IsIconic(robloxHwnd) ||
                !_desktop.GetClientRect(robloxHwnd, out var current) ||
                current.Width != clientW || current.Height != clientH ||
                _desktop.GetDpiForWindow(robloxHwnd) != initialDpi)
                throw new GameplayInterruptedException("Pre-flight interrupted: Roblox focus or viewport changed. Start again explicitly.");
        }
        ValidateContext();

        const int benchSamples = 5;
        double totalCaptureMs = 0;
        bool hasValidPixels = false;
        // Benchmark ROI: center-strip matching real fishing engine usage (~300px wide × 12% height)
        // Full-width captures on ultrawide (3424px) are 3-5x slower but the engine never does them
        int testRoiW = Math.Max(200, (int)Math.Round(clientH * 0.30));
        int testRoiX = Math.Max(0, (clientW / 2) - (testRoiW / 2));
        testRoiW = Math.Min(testRoiW, clientW - testRoiX);
        int testRoiH = Math.Max(50, (int)Math.Round(clientH * 0.12));
        int testRoiY = clientH - testRoiH;

        // Warm-up capture: pre-allocates DIBSection and GDI context so the first
        // timed sample isn't penalised by one-time setup cost (~10-30ms cold start).
        using (var warmUp = _capture.CaptureClientRegion(robloxHwnd, testRoiX, testRoiY, testRoiW, testRoiH))
        { /* discard */ }

        for (int i = 0; i < benchSamples; i++)
        {
            ValidateContext();
            var swSample = Stopwatch.StartNew();
            using var sample = _capture.CaptureClientRegion(robloxHwnd, testRoiX, testRoiY, testRoiW, testRoiH);
            swSample.Stop();
            totalCaptureMs += swSample.Elapsed.TotalMilliseconds;

            if (sample != null && !sample.Empty())
            {
                Scalar mean = Cv2.Mean(sample);
                if (mean.Val0 > 2 || mean.Val1 > 2 || mean.Val2 > 2)
                {
                    hasValidPixels = true;
                }
            }
        }

        double avgLatency = totalCaptureMs / benchSamples;
        stepCapture.DurationMs = swStep.Elapsed.TotalMilliseconds;

        if (!hasValidPixels)
        {
            stepCapture.Status = DiagnosticStatus.Fail;
            stepCapture.Metric = "Black / Empty Frames";
            stepCapture.Details = "Captured frames are completely black or empty. Ensure Roblox graphics are rendering and not occluded.";
        }
        else if (avgLatency <= 5.0)
        {
            stepCapture.Status = DiagnosticStatus.Pass;
            stepCapture.Metric = $"{avgLatency:F1}ms (Optimal)";
            stepCapture.Details = $"Hardware DWM BitBlt compositing running at ultra-fast {avgLatency:F1}ms. Excellent for real-time tracking.";
        }
        else if (avgLatency <= 20.0)
        {
            stepCapture.Status = DiagnosticStatus.Pass;
            stepCapture.Metric = $"{avgLatency:F1}ms (Acceptable)";
            stepCapture.Details = $"Capture latency is {avgLatency:F1}ms. Sufficient for high-speed tracking at 60+ FPS.";
        }
        else
        {
            stepCapture.Status = DiagnosticStatus.Warning;
            stepCapture.Metric = $"{avgLatency:F1}ms (High)";
            stepCapture.Details = $"Capture latency is {avgLatency:F1}ms. Reeling reaction times may be slightly affected under heavy GPU load.";
        }
        onStepUpdate?.Invoke(stepCapture);

        await Task.Delay(40, ct);

        // =========================================================================
        // STEP 3: Dynamic Hotbar & Slot Discovery
        // =========================================================================
        stepHotbar.Status = DiagnosticStatus.Running;
        onStepUpdate?.Invoke(stepHotbar);
        swStep.Restart();

        int bottomH = Math.Max(50, (int)Math.Round(clientH * 0.25));
        int bottomY = clientH - bottomH;

        ValidateContext();
        using var bottomSnap = _capture.CaptureClientRegion(robloxHwnd, 0, bottomY, clientW, bottomH);
        RodDetectionResult? initialRodRes = null;

        if (bottomSnap != null && !bottomSnap.Empty())
        {
            using var bgr = new Mat();
            if (bottomSnap.Channels() == 4)
                Cv2.CvtColor(bottomSnap, bgr, ColorConversionCodes.BGRA2BGR);
            else
                bottomSnap.CopyTo(bgr);

            initialRodRes = _vision.DetectRodEquipped(bgr, slotNum, generateDebug: false, fullViewportHeight: clientH);
        }

        stepHotbar.DurationMs = swStep.Elapsed.TotalMilliseconds;

        if (initialRodRes is { HotbarFound: true, GeometryConfirmed: true })
        {
            stepHotbar.Status = DiagnosticStatus.Pass;
            stepHotbar.Metric = $"Container [W:{initialRodRes.HotbarBounds.Width}px, H:{initialRodRes.HotbarBounds.Height}px]";
            stepHotbar.Details = $"Container discovered dynamically. Slot {slotNum} centered at X={initialRodRes.SlotCenter.X} (width={initialRodRes.SlotBounds.Width}px).";
        }
        else
        {
            stepHotbar.Status = DiagnosticStatus.Fail;
            stepHotbar.Metric = "Hotbar Not Confirmed";
            stepHotbar.Details = "Hotbar was not visually confirmed. No estimated slot clicks will be sent.";
        }
        onStepUpdate?.Invoke(stepHotbar);

        await Task.Delay(40, ct);

        // =========================================================================
        // STEP 4: Live Momentary Tool Toggle Probe
        // =========================================================================
        stepToggle.Status = DiagnosticStatus.Running;
        onStepUpdate?.Invoke(stepToggle);
        swStep.Restart();

        bool toggledEquipped = IsConfirmedEquipped(initialRodRes);
        if (!toggledEquipped && initialRodRes?.GeometryConfirmed == true && hasValidPixels)
        {
            // One guarded keypress, followed by fresh CV observations. Never use
            // generic pixel changes or guessed slot clicks as equipment evidence.
            var input = new GameplayInput(ValidateContext, ms =>
            {
                if (ct.WaitHandle.WaitOne(ms)) ct.ThrowIfCancellationRequested();
                ValidateContext();
            }, hardware: _hardware);
            try
            {
                input.SendKeyPress(rodKey);
                var probe = Stopwatch.StartNew();
                while (probe.ElapsedMilliseconds < 1000)
                {
                    await Task.Delay(40, ct);
                    ValidateContext();
                    using var snap = _capture.CaptureClientRegion(robloxHwnd, 0, bottomY, clientW, bottomH);
                    if (snap == null || snap.Empty())
                        throw new GameplayInterruptedException("Pre-flight capture became unavailable.");
                    using var bgr = new Mat();
                    if (snap.Channels() == 4) Cv2.CvtColor(snap, bgr, ColorConversionCodes.BGRA2BGR);
                    else snap.CopyTo(bgr);
                    var result = _vision.DetectRodEquipped(bgr, slotNum, generateDebug: false, fullViewportHeight: clientH);
                    toggledEquipped = IsConfirmedEquipped(result);
                    if (toggledEquipped) break;
                }
            }
            finally { input.ReleaseAll(); }
        }
        ValidateContext();
        stepToggle.DurationMs = swStep.Elapsed.TotalMilliseconds;
        stepToggle.Status = toggledEquipped ? DiagnosticStatus.Pass : DiagnosticStatus.Fail;
        stepToggle.Metric = toggledEquipped ? "Equipped (Visually Confirmed)" : "Equipment Not Confirmed";
        stepToggle.Details = toggledEquipped
            ? $"Slot {slotNum} is visibly equipped."
            : $"Could not confirm an equipped rod in slot {slotNum}. Pre-flight stopped without fallback clicks.";
        onStepUpdate?.Invoke(stepToggle);

        // =========================================================================
        // STEP 5: Coordinate Boundary & Safety Audit
        // =========================================================================
        stepSafety.Status = DiagnosticStatus.Running;
        onStepUpdate?.Invoke(stepSafety);
        swStep.Restart();

        ValidateContext();
        var audit = AuditCoordinates(clientW, clientH, initialRodRes?.SlotCenter.X);
        stepSafety.DurationMs = swStep.Elapsed.TotalMilliseconds;

        if (audit.AllSafe)
        {
            stepSafety.Status = DiagnosticStatus.Pass;
            stepSafety.Metric = $"{audit.Targets.Count}/{audit.Targets.Count} Cleared";
            stepSafety.Details = $"All {audit.Targets.Count} UI targeting coordinates verified inside client bounds and strictly below Roblox Topbar.";
        }
        else
        {
            stepSafety.Status = DiagnosticStatus.Fail;
            stepSafety.Metric = "Target Drift Detected";
            stepSafety.Details = "One or more targeting coordinates fall near window borders or into the Roblox Topbar.";
        }
        onStepUpdate?.Invoke(stepSafety);

        // =========================================================================
        // STEP 6: Diagnostic Snapshot Generation
        // =========================================================================
        ValidateContext();
        try
        {
            using var fullSnap = _capture.CaptureClientRegion(robloxHwnd, 0, 0, clientW, clientH);
            if (fullSnap != null && !fullSnap.Empty())
            {
                int recoveryW = Math.Min(clientW, (int)(clientH * .62));
                int recoveryH = Math.Min(clientH, (int)(clientH * .40));
                using (var recovery = new Mat(fullSnap, new Rect((clientW-recoveryW)/2, (clientH-recoveryH)/2, recoveryW, recoveryH)))
                {
                    report.ReconnectAvailable = DisconnectDetector.TryFindReconnect(recovery, clientH, out _);
                    report.ContinueAvailable = ContinueScreenDetector.IsContinueScreen(recovery, clientH);
                    report.DeathDetected = DeathScreenDetector.IsDeathScreen(recovery, clientH);
                }
                Mat annotated = new Mat();
                if (fullSnap.Channels() == 4)
                    Cv2.CvtColor(fullSnap, annotated, ColorConversionCodes.BGRA2BGR);
                else
                    fullSnap.CopyTo(annotated);

                // Draw Avatar Overhead Cast Bar ROI Box (Cyan)
                int castHalfW = (int)Math.Round(clientH * 0.28);
                int castX = Math.Max(0, (clientW / 2) - castHalfW);
                int castW = Math.Min(clientW - castX, castHalfW * 2);
                int castY = (int)Math.Round(clientH * 0.25);
                int castH = (int)Math.Round(clientH * 0.50);
                Cv2.Rectangle(annotated, new Rect(castX, castY, castW, castH), Scalar.FromRgb(0, 229, 255), 1);
                Cv2.PutText(annotated, "CAST POWER ROI", new Point(castX + 6, castY + 16), HersheyFonts.HersheySimplex, 0.40, Scalar.FromRgb(0, 229, 255), 1);

                // Draw Safe Water Crosshair (Gold)
                int waterX = clientW / 2;
                int waterY = clientH / 2;
                Cv2.DrawMarker(annotated, new Point(waterX, waterY), Scalar.FromRgb(255, 215, 0), MarkerTypes.Cross, 22, 2);
                Cv2.PutText(annotated, "WATER SAFE TARGET", new Point(waterX + 14, waterY + 4), HersheyFonts.HersheySimplex, 0.40, Scalar.FromRgb(255, 215, 0), 1);

                // Draw Hotbar Bounds (if discovered)
                if (initialRodRes != null && initialRodRes.HotbarFound)
                {
                    var hb = initialRodRes.HotbarBounds;
                    Rect absHb = new Rect(hb.X, bottomY + hb.Y, hb.Width, hb.Height);
                    Cv2.Rectangle(annotated, absHb, Scalar.FromRgb(56, 189, 248), 1);

                    var sb = initialRodRes.SlotBounds;
                    Rect absSb = new Rect(sb.X, bottomY + sb.Y, sb.Width, sb.Height);
                    Scalar slotColor = toggledEquipped ? Scalar.FromRgb(0, 230, 118) : Scalar.FromRgb(248, 113, 113);
                    Cv2.Rectangle(annotated, absSb, slotColor, 2);
                }

                // Header Banner
                bool passes = (stepWindow.Status != DiagnosticStatus.Fail && stepCapture.Status != DiagnosticStatus.Fail && stepHotbar.Status == DiagnosticStatus.Pass && stepToggle.Status != DiagnosticStatus.Fail && stepSafety.Status != DiagnosticStatus.Fail);
                Scalar bannerBg = passes ? Scalar.FromRgb(12, 46, 36) : Scalar.FromRgb(69, 26, 26);
                Scalar bannerFg = passes ? Scalar.FromRgb(52, 211, 153) : Scalar.FromRgb(248, 113, 113);
                Cv2.Rectangle(annotated, new Rect(10, 10, 360, 30), bannerBg, -1);
                Cv2.Rectangle(annotated, new Rect(10, 10, 360, 30), bannerFg, 1);
                string tag = passes ? "PRE-FLIGHT: READY FOR UNATTENDED AFK" : "PRE-FLIGHT: ATTENTION NEEDED";
                Cv2.PutText(annotated, tag, new Point(20, 30), HersheyFonts.HersheySimplex, 0.48, bannerFg, 2);

                report.AnnotatedSnapshot = annotated;
            }
        }
        catch { }

        // =========================================================================
        // Overall Summary & Verdict
        // =========================================================================
        bool overallPass = (!report.DeathDetected && stepWindow.Status == DiagnosticStatus.Pass &&
                            stepCapture.Status != DiagnosticStatus.Fail &&
                            stepHotbar.Status != DiagnosticStatus.Fail &&
                            stepToggle.Status != DiagnosticStatus.Fail &&
                            stepSafety.Status == DiagnosticStatus.Pass);

        report.OverallPass = overallPass;
        report.TotalDurationMs = swTotal.Elapsed.TotalMilliseconds;

        if (overallPass)
        {
            report.Summary = $"All systems nominal ({report.TotalDurationMs:F0}ms). Computer Vision, tool toggle, and coordinate safety verified. Ready for AFK! 🎣";
        }
        else if (report.DeathDetected)
        {
            report.Summary = "Character died. Respawn and return to a fishing spot before starting AFK.";
        }
        else if (report.ReconnectAvailable)
        {
            report.Summary = "Disconnected. Start Fishing will retry Reconnect and wait for gameplay.";
        }
        else if (report.ContinueAvailable)
        {
            report.Summary = "Fisch is awaiting Continue. Start Fishing will continue loading and wait for gameplay.";
        }
        else
        {
            report.Summary = "Pre-Flight check encountered issues. Please review the failed items before starting AFK.";
        }

        try { ValidateContext(); }
        catch { report.AnnotatedSnapshot?.Dispose(); throw; }
        return report;
    }

    public static bool IsConfirmedEquipped(RodDetectionResult? result) =>
        result is { HotbarFound: true, GeometryConfirmed: true, IsEquipped: true } &&
        result.SlotBounds.Width > 0 && result.SlotBounds.Height > 0;

    public static (bool AllSafe, List<(string Name, int X, int Y, bool IsSafe)> Targets) AuditCoordinates(int clientW, int clientH, int? slot1CenterX = null)
    {
        var rawTargets = new List<(string Name, int X, int Y)>
        {
            ("Water Casting Point", clientW / 2, clientH / 2),
            ("Slot 1 Hotbar Target", slot1CenterX ?? (clientW / 2 - (int)(clientH * 0.224)), clientH - (int)(clientH * 0.035)),
            ("Backpack Search Bar", (clientW / 2) + (int)Math.Round(clientH * 0.0498), (clientH / 2) + (int)Math.Round(clientH * 0.202)),
            ("Backpack Close Button", (clientW / 2) + (int)Math.Round(clientH * 0.1522), (clientH / 2) + (int)Math.Round(clientH * 0.172)),
            ("Crate Dialog [Yes]", (clientW / 2) - (int)Math.Round(clientH * 0.1173), (clientH / 2) + (int)Math.Round(clientH * 0.071)),
            ("Aquarium Close Button", (clientW / 2) + (int)Math.Round(clientH * 0.5602), (clientH / 2) - (int)Math.Round(clientH * 0.3795))
        };

        bool allCoordinatesSafe = true;
        int safeMarginX = 12;
        int minTopMarginY = (int)Math.Round(clientH * 0.045); // Below Roblox Topbar

        var results = new List<(string Name, int X, int Y, bool IsSafe)>();
        foreach (var t in rawTargets)
        {
            bool safe = !(t.X < safeMarginX || t.X > (clientW - safeMarginX) || t.Y < minTopMarginY || t.Y > (clientH - 4));
            if (!safe) allCoordinatesSafe = false;
            results.Add((t.Name, t.X, t.Y, safe));
        }

        return (allCoordinatesSafe, results);
    }

    public void Dispose()
    {
        if (_ownsCapture)
        {
            _capture.Dispose();
        }
    }
}
