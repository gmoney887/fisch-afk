using System;
using System.Collections.Generic;
using System.IO;
using OpenCvSharp;
using FischMacroCS.Core;
using FischMacroCS.Vision;
using Xunit;

namespace FischMacroCS.Tests;

public class PreFlightDiagnosticTests
{
    private readonly VisionProcessor _vision = new();

    [Theory]
    [InlineData(false, false, 20, false)]
    [InlineData(true, false, 20, false)]
    [InlineData(false, true, 20, false)]
    [InlineData(true, true, 0, false)]
    [InlineData(true, true, 20, true)]
    public void EquipmentConfirmationRequiresVisibleHotbarAndEquippedSlot(
        bool hotbar, bool equipped, int width, bool expected)
    {
        var result = new RodDetectionResult
        {
            HotbarFound = hotbar, GeometryConfirmed = hotbar, IsEquipped = equipped,
            SlotBounds = new Rect(100, 100, width, 20)
        };
        Assert.Equal(expected, PreFlightDiagnostic.IsConfirmedEquipped(result));
        Assert.False(PreFlightDiagnostic.IsConfirmedEquipped(null));
    }

    [Fact]
    public async Task CancelledDiagnosticDoesNotInspectOrActivateLiveGame()
    {
        using var diagnostic = new PreFlightDiagnostic(new Settings());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        int updates = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            diagnostic.RunDiagnosticAsync(_ => updates++, cancellation.Token));
        Assert.Equal(0, updates);
    }

    [Fact]
    public async Task PreFlight_ReportStepList_ContainsAllExpectedDiagnosticProbes()
    {
        var settings = new Settings();
        using var diag = new PreFlightDiagnostic(settings, _vision, desktop: new MissingDesktop());
        var report = await diag.RunDiagnosticAsync();

        Assert.Equal(5, report.Steps.Count);
        Assert.Equal("window", report.Steps[0].Id);
        Assert.Equal("capture", report.Steps[1].Id);
        Assert.Equal("hotbar", report.Steps[2].Id);
        Assert.Equal("tool_toggle", report.Steps[3].Id);
        Assert.Equal("coordinates", report.Steps[4].Id);
    }

    private sealed class MissingDesktop : GameDesktop
    {
        public override IntPtr FindRobloxWindow() => IntPtr.Zero;
    }

    [Theory]
    [InlineData(1920, 1080)]  // 1080p 16:9
    [InlineData(2560, 1440)]  // 1440p 16:9
    [InlineData(3840, 2160)]  // 4K UHD 16:9
    [InlineData(1280, 720)]   // 720p 16:9
    [InlineData(1920, 1200)]  // 16:10
    [InlineData(2560, 1080)]  // 21:9 Ultrawide
    [InlineData(3440, 1440)]  // 21:9 UWQHD
    [InlineData(5120, 1440)]  // 32:9 Super Ultrawide
    [InlineData(800, 600)]    // Small resolution
    public void AuditCoordinates_StandardResolutions_AllTargetsSafeAndBelowTopbar(int w, int h)
    {
        var audit = PreFlightDiagnostic.AuditCoordinates(w, h);

        Assert.True(audit.AllSafe, $"All UI targets should be safe at resolution {w}x{h}");
        Assert.Equal(6, audit.Targets.Count);

        int minTopMarginY = (int)Math.Round(h * 0.045);
        foreach (var (name, x, y, isSafe) in audit.Targets)
        {
            Assert.True(isSafe, $"Target '{name}' at ({x}, {y}) was marked unsafe for resolution {w}x{h}");
            Assert.InRange(x, 12, w - 12);
            Assert.InRange(y, minTopMarginY, h - 4);
        }
    }

    [Fact]
    public void AuditCoordinates_OutOfBounds_FlagsSafetyBreach()
    {
        // For an extremely narrow or degenerate viewport, safety check must flag breaches
        var audit = PreFlightDiagnostic.AuditCoordinates(50, 40);

        Assert.False(audit.AllSafe, "Degenerate resolution must fail coordinate safety audit");
        Assert.Contains(audit.Targets, t => !t.IsSafe);
    }

    [Fact]
    public void ToolToggleProbe_UnequippedToEquipped_DetectsTransitionCorrectly()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string fixturePath = Path.Combine(baseDir, "Fixtures", "user_screenshot.png");
        if (!File.Exists(fixturePath))
        {
            fixturePath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Fixtures", "user_screenshot.png"));
        }
        Assert.True(File.Exists(fixturePath), "Fixture must exist");

        using var unequippedImg = Cv2.ImRead(fixturePath);
        Assert.False(unequippedImg.Empty());

        // Step 1: Initial state (rod unequipped in user screenshot)
        var initialRes = _vision.DetectRodEquipped(unequippedImg, slotNum: 1, generateDebug: false);
        Assert.False(initialRes.IsEquipped, "Initial state should be unequipped");
        double initialDensity = initialRes.ActiveDensity;

        // Step 2: Simulate keypress '1' toggling rod equipped (illuminating slot 1 interior)
        using var toggledImg = unequippedImg.Clone();
        for (int y = 398; y <= 412; y++)
        {
            for (int x = 425; x <= 437; x++)
            {
                toggledImg.Set(y, x, new Vec3b(240, 160, 20)); // Active cyan highlight BGR
            }
        }

        var postToggleRes = _vision.DetectRodEquipped(toggledImg, slotNum: 1, generateDebug: false);
        Assert.True(postToggleRes.IsEquipped, "Post-toggle state should be equipped");
        double toggledDensity = postToggleRes.ActiveDensity;

        // Step 3: Verify OpenCV temporal differential analysis (Cv2.Absdiff)
        var diffRes = _vision.DetectToolToggleDiff(unequippedImg, toggledImg, initialRes.SlotBounds, minDeltaRatio: 0.03, generateDebug: true);
        Assert.True(diffRes.ToggleDetected, "OpenCV temporal differential analysis must detect visual state change");
        Assert.True(diffRes.DeltaRatio >= 0.03, $"Delta ratio must exceed 3%, got {diffRes.DeltaRatio:P2}");
        Assert.NotNull(diffRes.AnnotatedFrame);

        // Verify momentary toggle detection criteria
        bool stateChanged = (initialRes.IsEquipped != postToggleRes.IsEquipped) || diffRes.ToggleDetected;
        Assert.True(stateChanged, "State transition between unequipped and equipped MUST be detected");
        Assert.True(toggledDensity > initialDensity, "Toggled density must be greater than unequipped density");
        Assert.True(postToggleRes.IsEquipped, "Final state is confirmed equipped");
    }

    [Fact]
    public void DetectToolToggleDiff_AbsDiff_DetectsTemporalStateChange()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string fixturePath = Path.Combine(baseDir, "Fixtures", "user_screenshot.png");
        if (!File.Exists(fixturePath))
        {
            fixturePath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Fixtures", "user_screenshot.png"));
        }
        Assert.True(File.Exists(fixturePath), "Fixture must exist");

        using var beforeImg = Cv2.ImRead(fixturePath);
        Assert.False(beforeImg.Empty());

        var initialRes = _vision.DetectRodEquipped(beforeImg, slotNum: 1, generateDebug: false);
        Assert.True(initialRes.HotbarFound);

        // 1. Identical frames: Diff should be 0, no toggle
        var noChangeRes = _vision.DetectToolToggleDiff(beforeImg, beforeImg, initialRes.SlotBounds, minDeltaRatio: 0.03);
        Assert.False(noChangeRes.ToggleDetected, "Identical frames must not trigger toggle");
        Assert.Equal(0, noChangeRes.ChangedPixels);
        Assert.Equal(0.0, noChangeRes.DeltaRatio);

        // 2. Toggled frame: Illuminate slot 1
        using var afterImg = beforeImg.Clone();
        int y1 = initialRes.SlotBounds.Y + 4;
        int y2 = initialRes.SlotBounds.Bottom - 4;
        int x1 = initialRes.SlotBounds.X + 4;
        int x2 = initialRes.SlotBounds.Right - 4;
        for (int y = y1; y < y2; y++)
        {
            for (int x = x1; x < x2; x++)
            {
                afterImg.Set(y, x, new Vec3b(240, 160, 20));
            }
        }

        var toggleRes = _vision.DetectToolToggleDiff(beforeImg, afterImg, initialRes.SlotBounds, minDeltaRatio: 0.03, generateDebug: true);
        Assert.True(toggleRes.ToggleDetected, "Temporal visual delta must confirm toggle");
        Assert.True(toggleRes.ChangedPixels > 50, "Changed pixels should be significant");
        Assert.True(toggleRes.DeltaRatio >= 0.03, "Delta ratio must exceed 3%");
        Assert.NotNull(toggleRes.AnnotatedFrame);
    }

    [Fact]
    public void Settings_AutoRunPreFlightOnStart_DefaultsToTrueAndPersists()
    {
        var settings = new Settings();
        Assert.True(settings.AutoRunPreFlightOnStart, "AutoRunPreFlightOnStart should default to true for maximum safety");

        settings.AutoRunPreFlightOnStart = false;
        Assert.False(settings.AutoRunPreFlightOnStart);

        settings.AutoRunPreFlightOnStart = true;
        Assert.True(settings.AutoRunPreFlightOnStart);
    }
}
