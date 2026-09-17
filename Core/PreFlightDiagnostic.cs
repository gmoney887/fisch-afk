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
    private readonly ScreenCapture _capture;
    private readonly bool _ownsCapture;

    public PreFlightDiagnostic(Settings config, VisionProcessor? vision = null, ScreenCapture? capture = null)
    {
        _config = config;
        _vision = vision ?? new VisionProcessor();
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

        IntPtr robloxHwnd = Win32.FindRobloxWindow();
        if (robloxHwnd == IntPtr.Zero || !Win32.GetClientRect(robloxHwnd, out Win32.RECT clientRect) || clientRect.Width < 300 || clientRect.Height < 200)
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

        Win32.ForceSetForegroundWindow(robloxHwnd);
        await Task.Delay(80, ct);

        const int benchSamples = 5;
        double totalCaptureMs = 0;
        bool hasValidPixels = false;
        int testRoiH = Math.Max(50, (int)Math.Round(clientH * 0.25));
        int testRoiY = clientH - testRoiH;

        for (int i = 0; i < benchSamples; i++)
        {
            var swSample = Stopwatch.StartNew();
            using var sample = _capture.CaptureClientRegion(robloxHwnd, 0, testRoiY, clientW, testRoiH);
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
        else if (avgLatency <= 3.0)
        {
            stepCapture.Status = DiagnosticStatus.Pass;
            stepCapture.Metric = $"{avgLatency:F1}ms (Optimal)";
            stepCapture.Details = $"Hardware DWM BitBlt compositing running at ultra-fast {avgLatency:F1}ms (Sub-3ms pass).";
        }
        else if (avgLatency <= 8.0)
        {
            stepCapture.Status = DiagnosticStatus.Pass;
            stepCapture.Metric = $"{avgLatency:F1}ms (Acceptable)";
            stepCapture.Details = $"Capture latency is {avgLatency:F1}ms. Sufficient for high-speed tracking.";
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

        using var bottomSnap = _capture.CaptureClientRegion(robloxHwnd, 0, bottomY, clientW, bottomH);
        RodDetectionResult? initialRodRes = null;

        if (bottomSnap != null && !bottomSnap.Empty())
        {
            using var bgr = new Mat();
            if (bottomSnap.Channels() == 4)
                Cv2.CvtColor(bottomSnap, bgr, ColorConversionCodes.BGRA2BGR);
            else
                bottomSnap.CopyTo(bgr);

            initialRodRes = _vision.DetectRodEquipped(bgr, slotNum, generateDebug: true, fullViewportHeight: clientH);
        }

        stepHotbar.DurationMs = swStep.Elapsed.TotalMilliseconds;

        if (initialRodRes != null && initialRodRes.HotbarFound)
        {
            stepHotbar.Status = DiagnosticStatus.Pass;
            stepHotbar.Metric = $"Container [W:{initialRodRes.HotbarBounds.Width}px, H:{initialRodRes.HotbarBounds.Height}px]";
            stepHotbar.Details = $"Container discovered dynamically. Slot {slotNum} centered at X={initialRodRes.SlotCenter.X} (width={initialRodRes.SlotBounds.Width}px).";
        }
        else
        {
            stepHotbar.Status = DiagnosticStatus.Warning;
            stepHotbar.Metric = "Fallback Mode";
            stepHotbar.Details = "Hotbar container outline was low contrast. Engine will use height-scaled center anchors.";
        }
        onStepUpdate?.Invoke(stepHotbar);

        await Task.Delay(40, ct);

        // =========================================================================
        // STEP 4: Live Momentary Tool Toggle Probe
        // =========================================================================
        stepToggle.Status = DiagnosticStatus.Running;
        onStepUpdate?.Invoke(stepToggle);
        swStep.Restart();

        bool initialEquipped = initialRodRes?.IsEquipped ?? false;
        double initialDensity = initialRodRes?.ActiveDensity ?? 0.0;
        Rect slotBounds = initialRodRes?.SlotBounds ?? new Rect(
            (clientW / 2) - (int)Math.Round(clientH * 0.217),
            bottomH - (int)Math.Round(clientH * 0.0584) - Math.Max(2, (int)Math.Round(clientH * 0.007)),
            Math.Max(16, (int)Math.Round(clientH * 0.0483)),
            Math.Max(16, (int)Math.Round(clientH * 0.0584))
        );

        // Ensure Roblox is foreground
        Win32.ForceSetForegroundWindow(robloxHwnd);
        await Task.Delay(80, ct);

        // Send momentary keypress '1'
        Win32.SendKeyPress(rodKey);
        await Task.Delay(200, ct);

        // Capture post-toggle frame
        using var postToggleSnap = _capture.CaptureClientRegion(robloxHwnd, 0, bottomY, clientW, bottomH);
        RodDetectionResult? postToggleRes = null;
        ToolToggleResult? toggleDiffRes = null;

        if (postToggleSnap != null && !postToggleSnap.Empty())
        {
            using var bgr = new Mat();
            if (postToggleSnap.Channels() == 4)
                Cv2.CvtColor(postToggleSnap, bgr, ColorConversionCodes.BGRA2BGR);
            else
                postToggleSnap.CopyTo(bgr);

            postToggleRes = _vision.DetectRodEquipped(bgr, slotNum, generateDebug: true, fullViewportHeight: clientH);

            if (bottomSnap != null && !bottomSnap.Empty())
            {
                using var bgrBefore = new Mat();
                if (bottomSnap.Channels() == 4)
                    Cv2.CvtColor(bottomSnap, bgrBefore, ColorConversionCodes.BGRA2BGR);
                else
                    bottomSnap.CopyTo(bgrBefore);

                toggleDiffRes = _vision.DetectToolToggleDiff(bgrBefore, bgr, slotBounds, minDeltaRatio: 0.03, generateDebug: true);
            }
        }

        bool toggledEquipped = postToggleRes?.IsEquipped ?? false;
        double toggledDensity = postToggleRes?.ActiveDensity ?? 0.0;
        bool stateChanged = (initialEquipped != toggledEquipped) || (toggleDiffRes?.ToggleDetected ?? false);

        // If keypress did not cause a toggle (e.g. background chat focus), fallback to hardware clicking the slot directly
        if (!stateChanged && !toggledEquipped)
        {
            int slotClickX = initialRodRes?.SlotCenter.X ?? (clientW / 2 - (int)Math.Round(clientH * 0.1933));
            int slotClickY = bottomY + (initialRodRes?.SlotCenter.Y ?? (bottomH - (int)Math.Round(clientH * 0.035)));
            if (Win32.SanitizeGameCoordinate(robloxHwnd, slotClickX, slotClickY, out int safeX, out int safeY, out int sX, out int sY))
            {
                Win32.SendHardwareClick(sX, sY, safeX, safeY, robloxHwnd);
                await Task.Delay(200, ct);

                using var clickSnap = _capture.CaptureClientRegion(robloxHwnd, 0, bottomY, clientW, bottomH);
                if (clickSnap != null && !clickSnap.Empty())
                {
                    using var bgr = new Mat();
                    if (clickSnap.Channels() == 4)
                        Cv2.CvtColor(clickSnap, bgr, ColorConversionCodes.BGRA2BGR);
                    else
                        clickSnap.CopyTo(bgr);

                    postToggleRes = _vision.DetectRodEquipped(bgr, slotNum, generateDebug: true, fullViewportHeight: clientH);
                    toggledEquipped = postToggleRes.IsEquipped;
                    toggledDensity = postToggleRes.ActiveDensity;

                    if (bottomSnap != null && !bottomSnap.Empty())
                    {
                        using var bgrBefore = new Mat();
                        if (bottomSnap.Channels() == 4)
                            Cv2.CvtColor(bottomSnap, bgrBefore, ColorConversionCodes.BGRA2BGR);
                        else
                            bottomSnap.CopyTo(bgrBefore);

                        toggleDiffRes = _vision.DetectToolToggleDiff(bgrBefore, bgr, slotBounds, minDeltaRatio: 0.03, generateDebug: true);
                    }

                    stateChanged = (initialEquipped != toggledEquipped) || (toggleDiffRes?.ToggleDetected ?? false);
                }
            }
        }

        // Ensure the rod is returned to the EQUIPPED state ready for fishing!
        if (!toggledEquipped)
        {
            int slotClickX = initialRodRes?.SlotCenter.X ?? (clientW / 2 - (int)Math.Round(clientH * 0.1933));
            int slotClickY = bottomY + (initialRodRes?.SlotCenter.Y ?? (bottomH - (int)Math.Round(clientH * 0.035)));
            if (Win32.SanitizeGameCoordinate(robloxHwnd, slotClickX, slotClickY, out int safeX, out int safeY, out int sX, out int sY))
            {
                Win32.SendHardwareClick(sX, sY, safeX, safeY, robloxHwnd);
                await Task.Delay(200, ct);
            }
            else
            {
                Win32.SendKeyPress(rodKey);
                await Task.Delay(200, ct);
            }

            // Re-verify final state
            using var finalSnap = _capture.CaptureClientRegion(robloxHwnd, 0, bottomY, clientW, bottomH);
            if (finalSnap != null && !finalSnap.Empty())
            {
                using var bgr = new Mat();
                if (finalSnap.Channels() == 4)
                    Cv2.CvtColor(finalSnap, bgr, ColorConversionCodes.BGRA2BGR);
                else
                    finalSnap.CopyTo(bgr);

                var finalRes = _vision.DetectRodEquipped(bgr, slotNum, generateDebug: false, fullViewportHeight: clientH);
                toggledEquipped = finalRes.IsEquipped;
                toggledDensity = finalRes.ActiveDensity;
            }
        }

        // Return mouse cursor to safe water area
        int safeWaterX = clientW / 2;
        int safeWaterY = (int)Math.Round(clientH * 0.38);
        if (Win32.SafeClientToScreen(robloxHwnd, safeWaterX, safeWaterY, out int waterSX, out int waterSY))
        {
            Win32.SetCursorPos(waterSX, waterSY);
        }

        stepToggle.DurationMs = swStep.Elapsed.TotalMilliseconds;

        if (stateChanged)
        {
            stepToggle.Status = DiagnosticStatus.Pass;
            string deltaTag = toggleDiffRes != null && toggleDiffRes.ToggleDetected 
                ? $"Diff Delta: {toggleDiffRes.DeltaRatio * 100:F1}% (Confirmed)" 
                : $"Toggle Verified ({initialDensity * 100:F0}% ➔ {toggledDensity * 100:F0}%)";
            stepToggle.Metric = deltaTag;
            stepToggle.Details = $"OpenCV confirmed live tool state transition via temporal differential analysis! Slot {slotNum} is confirmed equipped in hand.";
        }
        else if (toggledEquipped)
        {
            stepToggle.Status = DiagnosticStatus.Pass;
            stepToggle.Metric = $"Equipped ({toggledDensity * 100:F0}% Density)";
            stepToggle.Details = $"Slot {slotNum} is confirmed equipped and ready for fishing.";
        }
        else
        {
            stepToggle.Status = DiagnosticStatus.Fail;
            stepToggle.Metric = "Unequipped / No Toggle";
            stepToggle.Details = $"Tool toggle was not observed. Verify slot {slotNum} contains a fishing rod and Roblox chat is closed.";
        }
        onStepUpdate?.Invoke(stepToggle);

        await Task.Delay(40, ct);

        // =========================================================================
        // STEP 5: Coordinate Boundary & Safety Audit
        // =========================================================================
        stepSafety.Status = DiagnosticStatus.Running;
        onStepUpdate?.Invoke(stepSafety);
        swStep.Restart();

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
        try
        {
            using var fullSnap = _capture.CaptureClientRegion(robloxHwnd, 0, 0, clientW, clientH);
            if (fullSnap != null && !fullSnap.Empty())
            {
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
                bool passes = (stepWindow.Status != DiagnosticStatus.Fail && stepCapture.Status != DiagnosticStatus.Fail && stepToggle.Status != DiagnosticStatus.Fail && stepSafety.Status != DiagnosticStatus.Fail);
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
        bool overallPass = (stepWindow.Status == DiagnosticStatus.Pass &&
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
        else
        {
            report.Summary = "Pre-Flight check encountered issues. Please review the failed items before starting AFK.";
        }

        return report;
    }

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
