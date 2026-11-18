namespace FischMacroCS.Core;

/// <summary>Reassert a potentially dropped held input only after sustained lack of response.</summary>
public sealed class ReelHoldRecovery(IClock clock)
{
    private long? _started;
    private long? _lastRetry;
    private double _startCenter;
    private int _retries;
    public void Reset() { _started = _lastRetry = null; _retries = 0; }
    public bool Observe(bool verified, bool holdingRight, double center, double fish, double width, double barVelocity = 0, double prevBarVelocity = 0)
    {
        if (!verified || !holdingRight || width <= 0 || fish <= center + width * .45)
        { _started = null; return false; }
        if (_retries >= 2 || _lastRetry.HasValue && clock.ElapsedMilliseconds(_lastRetry.Value) < 1000) return false;

        // If bar is actively moving right or accelerating rightward, rod pull is demonstrably working.
        if (barVelocity > 15 || (prevBarVelocity != 0 && barVelocity > prevBarVelocity + 15))
        {
            _started = clock.Timestamp;
            _startCenter = center;
            return false;
        }

        if (!_started.HasValue) { _started = clock.Timestamp; _startCenter = center; return false; }

        // Fast leftward motion alone cannot prove that the requested hold reached
        // the game. Allow momentum a bounded grace period; actual rightward
        // acceleration above renews it, but a dropped hold must still be retried.
        if (barVelocity < -40 && clock.ElapsedMilliseconds(_started.Value) < 750)
        {
            _startCenter = Math.Min(_startCenter, center);
            return false;
        }

        if (center < _startCenter)
        {
            _startCenter = center;
        }
        else if (center > _startCenter + Math.Max(2, width * .01))
        {
            _started = clock.Timestamp; _startCenter = center; return false;
        }

        if (clock.ElapsedMilliseconds(_started.Value) < 450) return false;
        _started = null; _lastRetry = clock.Timestamp; _retries++;
        return true;
    }
}
