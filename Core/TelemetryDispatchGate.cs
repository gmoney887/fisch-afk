namespace FischMacroCS.Core;

/// <summary>Drops busy preview updates, but always delivers terminal state.</summary>
public sealed class TelemetryDispatchGate
{
    private readonly IClock _clock;
    private readonly int _minimumIntervalMs;
    private long _lastDelivery;
    private MacroState? _lastState;
    private int _pending;
    private readonly object _gate = new();
    public TelemetryDispatchGate(int minimumIntervalMs = 0, IClock? clock = null)
    { _minimumIntervalMs = minimumIntervalMs; _clock = clock ?? new MonotonicClock(); }

    public bool TryEnter(MacroState state, out bool ownsPending)
    {
        lock (_gate)
        {
            ownsPending = false;
            if (state != MacroState.Stopped && state == _lastState && _clock.ElapsedMilliseconds(_lastDelivery) < _minimumIntervalMs)
                return false;
            ownsPending = _pending == 0;
            if (!ownsPending && state != MacroState.Stopped) return false;
            if (ownsPending) _pending = 1;
            _lastDelivery = _clock.Timestamp;
            _lastState = state;
            return true;
        }
    }

    public void Complete(bool ownsPending)
    {
        lock (_gate) { if (ownsPending) _pending = 0; }
    }
}
