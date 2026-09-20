namespace FischMacroCS.Core;

/// <summary>Pure cycle boundary policy shared by live execution and sequence replay tests.</summary>
public static class FishingCyclePolicy
{
    public static bool CanFinalize(bool confirmedBanner, int readyHotbarFrames, double elapsedMs) =>
        (confirmedBanner && readyHotbarFrames >= 2) || elapsedMs >= 1500;
}
