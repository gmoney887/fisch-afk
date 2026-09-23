namespace FischMacroCS.Core;

/// <summary>Retains a completed check across restarts, then uses monotonic elapsed time.</summary>
public sealed class AquariumSchedule(IClock clock)
{
    private long? _started;
    private double _initialAgeMs;
    private long? _retryStarted;
    private int _retryDelayMs;
    public bool IsDue(DateTime lastCheckUtc, int intervalMinutes, DateTime utcNow)
    {
        if (_retryStarted.HasValue && clock.ElapsedMilliseconds(_retryStarted.Value) < _retryDelayMs) return false;
        if (_started == null)
        {
            _started = clock.Timestamp;
            _initialAgeMs = lastCheckUtc == DateTime.MinValue ? double.PositiveInfinity
                : Math.Max(0, (utcNow - lastCheckUtc).TotalMilliseconds);
        }
        return _initialAgeMs + clock.ElapsedMilliseconds(_started.Value) >= Math.Max(5, intervalMinutes) * 60000L;
    }
    public void Checked()
    {
        _retryStarted = null;
        _retryDelayMs = 0;
        _started = clock.Timestamp;
        _initialAgeMs = 0;
    }
    public void Defer()
    {
        _retryStarted = clock.Timestamp;
        _retryDelayMs = _retryDelayMs == 0 ? 60000 : Math.Min(15 * 60000, _retryDelayMs * 2);
    }
}
