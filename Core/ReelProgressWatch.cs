namespace FischMacroCS.Core;

/// <summary>A reel stalls when its verified fill stops advancing, not when a slow catch ages.</summary>
public sealed class ReelProgressWatch(IClock clock)
{
    private long _lastAdvance;
    private double? _highWater, _candidate;
    private const double Step = .0025; // About 3.4 pixels at the recorded 1369px viewport.

    public void Reset() { _lastAdvance=clock.Timestamp; _highWater=_candidate=null; }
    public bool IsStalled(int timeoutMs) => clock.ElapsedMilliseconds(_lastAdvance)>Math.Max(1,timeoutMs);

    public bool Observe(double? fill)
    {
        if(!fill.HasValue || !double.IsFinite(fill.Value) || fill<=0 || fill>.57)
        { _candidate=null; return false; }
        if(!_highWater.HasValue) { _highWater=fill; return false; }
        if(fill<_highWater+Step) { _candidate=null; return false; }
        // Two fresh observations must agree on new forward progress. One spurious
        // fill jump, bar motion, or oscillation below the high-water mark cannot
        // keep a frozen/struggling reel alive indefinitely.
        if(_candidate.HasValue && fill>=_candidate-.001)
        {
            _highWater=Math.Min(fill.Value,_candidate.Value);
            _candidate=null;
            _lastAdvance=clock.Timestamp;
            return true;
        }
        _candidate=fill;
        return false;
    }
}
