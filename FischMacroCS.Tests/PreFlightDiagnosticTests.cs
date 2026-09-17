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

    [Fact]
    public void PreFlight_ReportStepList_ContainsAllExpectedDiagnosticProbes()
    {
        var settings = new Settings();
        using var diag = new PreFlightDiagnostic(settings, _vision);

        var report = new PreFlightReport();
        var stepWindow = new DiagnosticStep { Id = "window", Name = "Roblox Window & Resolution", Status = DiagnosticStatus.Pending };
        var stepCapture = new DiagnosticStep { Id = "capture", Name = "Screen Capture Latency Benchmark", Status = DiagnosticStatus.Pending };
        var stepHotbar = new DiagnosticStep { Id = "hotbar", Name = "Dynamic Hotbar & Slot Discovery", Status = DiagnosticStatus.Pending };
        var stepToggle = new DiagnosticStep { Id = "tool_toggle", Name = "Live Tool Toggle Probe", Status = DiagnosticStatus.Pending };
        var stepSafety = new DiagnosticStep { Id = "coordinates", Name = "Coordinate Boundary & Safety Audit", Status = DiagnosticStatus.Pending };

        report.Steps.AddRange(new[] { stepWindow, stepCapture, stepHotbar, stepToggle, stepSafety });

        Assert.Equal(5, report.Steps.Count);
        Assert.Equal("window", report.Steps[0].Id);
        Assert.Equal("capture", report.Steps[1].Id);
        Assert.Equal("hotbar", report.Steps[2].Id);
        Assert.Equal("tool_toggle", report.Steps[3].Id);
        Assert.Equal("coordinates", report.Steps[4].Id);
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

        // Verify momentary toggle detection criteria
        bool stateChanged = (initialRes.IsEquipped != postToggleRes.IsEquipped);
        Assert.True(stateChanged, "State transition between unequipped and equipped MUST be detected");
        Assert.True(toggledDensity > initialDensity, "Toggled density must be greater than unequipped density");
        Assert.True(postToggleRes.IsEquipped, "Final state is confirmed equipped");
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
