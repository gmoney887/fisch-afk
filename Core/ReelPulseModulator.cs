namespace FischMacroCS.Core;

/// <summary>Pulse density based on elapsed held time, not the number of vision frames.</summary>
public sealed class ReelPulseModulator
{
    private double _balance, _desired, _time, _decisionTime, _period = .05;
    private bool _held, _hasTime, _hasDecision;
    public double Desired => _desired;
    public void Reset()
    {
        _balance = _desired = 0; _held = _hasTime = _hasDecision = false; _period = .05;
    }
    public void Input(double nowMs, bool down) { Advance(nowMs); _held = down; }
    public void Force(double nowMs, bool down)
    {
        Advance(nowMs); ObservePeriod(nowMs);
        _balance = 0; _desired = down ? 1 : 0;
    }
    public bool Choose(double nowMs, double throttle)
    {
        Advance(nowMs); ObservePeriod(nowMs);
        _desired = Math.Clamp(throttle, 0, 1);
        // A saturated command is explicit pursuit/braking, not a request to
        // repay old fractional hover time. Debt from a released/missing-input
        // interval must never keep pull held after full release is requested,
        // or postpone full pull while the bar is escaping to the left.
        if (_desired is 0 or 1)
        {
            _balance = 0;
            return _desired == 1;
        }
        // Choose the command whose expected next held interval leaves the
        // smaller time debt. Actual input edges settle that debt on arrival.
        return _balance + _desired * _period >= _period / 2;
    }
    private void Advance(double nowMs)
    {
        if (_hasTime)
        {
            double elapsed = (nowMs - _time) / 1000;
            if (elapsed < 0) { _balance = 0; _hasDecision = false; }
            else _balance = Math.Clamp(_balance + (_desired - (_held ? 1 : 0)) * Math.Min(elapsed, 1), -.5, .5);
        }
        _time = nowMs; _hasTime = true;
    }
    private void ObservePeriod(double nowMs)
    {
        if (_hasDecision)
        {
            double elapsed = (nowMs - _decisionTime) / 1000;
            if (elapsed > 0)
            {
                double period = Math.Clamp(elapsed, .05, .25);
                _period += .35 * (period - _period);
            }
        }
        _decisionTime = nowMs; _hasDecision = true;
    }
}
