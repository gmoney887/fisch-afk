namespace FischMacroCS.Core;

/// <summary>Retains a completed check across restarts, then uses monotonic elapsed time.</summary>
public sealed class AquariumSchedule(IClock clock)
{
    private long? _started;
    private double _initialAgeMs;
    public bool IsDue(DateTime lastCheckUtc, int intervalMinutes, DateTime utcNow)
    {
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
        _started = clock.Timestamp;
        _initialAgeMs = 0;
    }
}
