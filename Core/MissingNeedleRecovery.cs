namespace FischMacroCS.Core;

/// <summary>A bar-shaped object alone must not sustain needle-search inputs indefinitely.</summary>
public sealed class MissingNeedleRecovery
{
    private double? _missingSince;
    public void Reset() => _missingSince = null;

    public bool Observe(bool barFound, bool fishFound, double elapsedMs)
    {
        if (!barFound || fishFound) { Reset(); return false; }
        _missingSince ??= elapsedMs;
        return elapsedMs - _missingSince.Value >= 3000;
    }
}
