namespace FischMacroCS.Core;

/// <summary>A lingering loss banner belongs to one attempt, not every following reel.</summary>
public sealed class FreshFailureEvidence
{
    private readonly IClock _clock;
    private int _present;
    private long? _absentSince;
    private bool _armed = true;
    public FreshFailureEvidence(IClock? clock=null) { _clock=clock??new MonotonicClock(); }
    public void Reset() { _present = 0; _absentSince=null; _armed = true; }
    public bool Observe(bool present)
    {
        if (!present)
        {
            _present = 0;
            _absentSince??=_clock.Timestamp;
            // Two low-confidence frames can be a short animation/overlay gap
            // in the same banner. Require a sustained clear interval to rearm.
            if (_clock.ElapsedMilliseconds(_absentSince.Value)>=500) _armed = true;
            return false;
        }
        _absentSince = null;
        if (++_present < 2 || !_armed) return false;
        _armed = false;
        return true;
    }
}
