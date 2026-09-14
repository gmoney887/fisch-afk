using System;
using System.IO;
using System.Text.Json;

namespace FischMacroCS.Core;

public class Settings
{
    public string RodSlot { get; set; } = "1";
    public int CastHoldMs { get; set; } = 760; // Calibrated universal sweet spot for 100% PERFECT! cast
    public int PostCastDelayMs { get; set; } = 1000;
    public int PostCatchDelayMs { get; set; } = 1500; // 1500ms allows Roblox catch celebration & rod reset animation to complete
    public int LureTimeoutMs { get; set; } = 45000;
    public int ReelTimeoutMs { get; set; } = 45000;

    public bool EnableShakeClicks { get; set; } = true;
    public string ShakeMode { get; set; } = "Visual"; // "Visual", "Navigation", "Disabled"
    public int ShakeClickIntervalMs { get; set; } = 50;
    public int ShakeRepeatBypass { get; set; } = 8;
    public double ShakeAreaMarginX { get; set; } = 0.20;
    public double ShakeAreaMarginY { get; set; } = 0.08;

    public int HoverDownMs { get; set; } = 22;
    public int HoverUpMs { get; set; } = 30;
    public int EdgeSafetyMargin { get; set; } = 14;
    public int Deadzone { get; set; } = 10;

    public string ToggleHotkey { get; set; } = "F6";
    public string ReEquipHotkey { get; set; } = "F7";

    public bool ShowOverlay { get; set; } = true;
    public bool ShowVisionPreview { get; set; } = true;
    public bool EnableRecording { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
    public int MaxRecordingsToKeep { get; set; } = 25;

    // Advanced AFK & Humanization
    public string RodProfile { get; set; } = "Standard";
    public bool EnableHumanizedJitter { get; set; } = true;
    public bool EnableAntiAfk { get; set; } = true;
    public int AntiAfkIntervalMinutes { get; set; } = 8;
    public double CustomPullAccel { get; set; } = 550.0;
    public double CustomGravityFall { get; set; } = 410.0;

    // Aquarium Auto-Claim
    public bool EnableAutoClaimAquarium { get; set; } = true;
    public int AquariumClaimIntervalMinutes { get; set; } = 60;
    public DateTime LastAquariumClaimUtc { get; set; } = DateTime.MinValue;

    // Auto Crate Opener ('G' Equipment Menu)
    public bool EnableAutoOpenCrates { get; set; } = false;
    public int CrateIntervalCatches { get; set; } = 15;
    public int CrateMaxTypes { get; set; } = 0; // 0 = All crates until empty
    public int CrateStartSlot { get; set; } = 2;
    public int CrateEndSlot { get; set; } = 7;
    public int CrateSecondsPerSlot { get; set; } = 5;

    // Dynamic Perfect Cast Vision & Auto-Tuning
    public bool EnableDynamicCastRelease { get; set; } = true;
    public bool EnableAutoTuneCastDelay { get; set; } = true;
    public int CastPredictiveLeadMs { get; set; } = 25; // Predictive lead (ms) so mouse release lands at 97-99% sweet spot

    private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                var settings = JsonSerializer.Deserialize<Settings>(json);
                if (settings != null)
                {
                    // Default migration if empty or legacy
                    if (string.IsNullOrEmpty(settings.ToggleHotkey) || settings.ToggleHotkey == "F2")
                        settings.ToggleHotkey = "F6";
                    if (string.IsNullOrEmpty(settings.ReEquipHotkey) || settings.ReEquipHotkey == "F3")
                        settings.ReEquipHotkey = "F7";
                    if (settings.PostCatchDelayMs < 800 || settings.PostCatchDelayMs == 2000)
                        settings.PostCatchDelayMs = 1500;
                    if (settings.CastHoldMs <= 0)
                        settings.CastHoldMs = 760;
                    if (settings.CastPredictiveLeadMs <= 0)
                        settings.CastPredictiveLeadMs = 25;
                    if (string.IsNullOrEmpty(settings.ShakeMode))
                        settings.ShakeMode = settings.EnableShakeClicks ? "Visual" : "Disabled";
                    if (string.IsNullOrEmpty(settings.RodProfile))
                        settings.RodProfile = "Standard";
                    return settings;
                }
            }
        }
        catch { }

        var def = new Settings();
        def.Save();
        return def;
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }
}
