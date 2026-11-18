namespace FischMacroCS.Core;

public readonly record struct ReelLaserZone(int Left, int Right);

/// <summary>Temporarily keeps the entire control bar on one clear side of a pulse.</summary>
public sealed class ReelLaserAvoidance
{
    private ReelLaserZone? _zone;
    private double _until;
    private bool _left;

    public void Reset() => _zone = null;

    public bool Overlaps(double center, double width) => _zone is { } zone &&
        center + width / 2 > zone.Left && center - width / 2 < zone.Right;

    public bool TryTarget(double nowMs, ReelLaserZone? warning, double bar, double width,
        double fish, double captureLeft, double captureRight, out double target)
    {
        target = fish;
        if (!double.IsFinite(nowMs + bar + width + fish + captureLeft + captureRight) || width <= 0)
        { Reset(); return false; }
        if (warning is { } next && next.Right > next.Left)
        {
            bool newPulse = !_zone.HasValue || nowMs > _until ||
                Math.Abs(next.Left - _zone.Value.Left) > width / 2 || Math.Abs(next.Right - _zone.Value.Right) > width / 2;
            _zone = next;
            // Cover the brief clear phase of the flashing warning. This expires
            // independently of detections and is reset at every reel transition.
            _until = nowMs + 200;
            if (newPulse) _left = bar < (next.Left + next.Right) / 2.0;
        }
        if (_zone is not { } zone || nowMs > _until) { Reset(); return false; }
        double half = width / 2, margin = Math.Max(8, width * .1);
        // The ordinary capture is wider than the playable rail. Its scenery
        // padding must not become an impossible escape lane near an end.
        double inset = (captureRight - captureLeft) * .041;
        double minimum = captureLeft + inset + half, maximum = captureRight - inset - half;
        double left = Math.Min(maximum, zone.Left - half - margin);
        double right = Math.Max(minimum, zone.Right + half + margin);
        bool canLeft = left >= minimum, canRight = right <= maximum;
        if (!canLeft && !canRight) { Reset(); return false; }
        if (!canLeft) _left = false;
        if (!canRight) _left = true;
        target = _left ? Math.Clamp(fish, minimum, left) : Math.Clamp(fish, right, maximum);
        return true;
    }
}
