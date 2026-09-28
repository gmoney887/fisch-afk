namespace FischMacroCS.Core;

/// <summary>A lingering loss banner belongs to one attempt, not every following reel.</summary>
public sealed class FreshFailureEvidence
{
    private int _present, _absent;
    private bool _armed = true;
    public void Reset() { _present = _absent = 0; _armed = true; }
    public bool Observe(bool present)
    {
        if (!present)
        {
            _present = 0;
            if (++_absent >= 2) _armed = true;
            return false;
        }
        _absent = 0;
        if (++_present < 2 || !_armed) return false;
        _armed = false;
        return true;
    }
}
