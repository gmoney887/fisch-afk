namespace FischMacroCS.Core;

/// <summary>
/// Bounded execution of a command from current reel evidence. These positions
/// are control estimates only and never establish detections or reel outcomes.
/// Actual guarded edges, rather than requested pulses, advance the model.
/// </summary>
public sealed class ReelActuationPlan
{
    private bool _ready, _down;
    private double _bar, _velocity, _fish, _fishVelocity, _fishAcceleration, _pull, _fall, _at, _until;
    private double _minimumBar, _maximumBar, _minimumFish, _maximumFish;
    private double _decisionAt;
    private double _centerNoise;
    private double _disturbance;
    public bool Active => _ready;

    public void Reset() => _ready = false;

    public void Configure(double nowMs, double bar, double velocity, double fish, double fishVelocity,
        double pull, double fall, bool held, double fishAcceleration = 0,
        double trackLeft = double.NegativeInfinity, double trackRight = double.PositiveInfinity, double barHalfWidth = 0,
        double disturbanceAcceleration = 0)
    {
        _bar = bar; _velocity = velocity; _fish = fish; _fishVelocity = fishVelocity;
        _pull = pull; _fall = fall; _down = held; _at = nowMs; _until = nowMs + 250;
        _fishAcceleration = fishAcceleration;
        _disturbance = disturbanceAcceleration;
        _decisionAt = nowMs;
        _centerNoise = Math.Clamp(barHalfWidth * .006, .5, 2);
        // Include the independently current positions even if an imperfect
        // measured rail is slightly short. Clamp only subsequent predictions.
        _minimumBar = Math.Min(trackLeft + barHalfWidth, bar); _maximumBar = Math.Max(trackRight - barHalfWidth, bar);
        _minimumFish = Math.Min(trackLeft, fish); _maximumFish = Math.Max(trackRight, fish);
        _ready = double.IsFinite(nowMs + bar + velocity + fish + fishVelocity + pull + fall + fishAcceleration + disturbanceAcceleration) && pull > 0 && fall > 0;
    }

    public void Input(double nowMs, bool held)
    {
        if (_ready) Advance(nowMs);
        _down = held;
    }

    public bool TryDuty(double nowMs, out double duty)
    {
        duty = 0;
        if (!_ready || !double.IsFinite(nowMs) || nowMs < _at || nowMs > _until) { Reset(); return false; }
        double period = Math.Clamp((nowMs - _decisionAt) / 1000, .016, .05); _decisionAt = nowMs;
        Advance(nowMs);
        double error = _fish - _bar, relativeVelocity = _velocity - _fishVelocity;
        double brake = error >= 0 ? Math.Max(_fall * .5, _fall + _fishAcceleration - _disturbance)
            : Math.Max(_pull * .5, _pull - _fishAcceleration + _disturbance);
        double remaining = Math.Max(0, Math.Abs(error) - Math.Abs(relativeVelocity) * period - _centerNoise);
        // Linear PD alone can demand more velocity than bounded release/pull
        // can stop. Keep its desired relative velocity inside the CURRENT
        // stopping-distance envelope, including the next actuator interval.
        const double positionGain = 10, velocityGain = 4 * positionGain;
        double relativeTarget = Math.Sign(error) * Math.Min(positionGain * Math.Abs(error), Math.Sqrt(2 * brake * remaining));
        double desiredVelocity = Math.Clamp(_fishVelocity + relativeTarget, -850, 850);
        // Critical damping: Kv=4*Kp, natural frequency2*Kp. Current input/force
        // and stopping limits bound every command; observation history fixes
        // must not change the shared response, fractional hover or evidence
        // lease to conceal an inaccurate target trajectory.
        duty = Math.Clamp((_fall + _fishAcceleration - _disturbance + velocityGain * (desiredVelocity - _velocity)) / (_pull + _fall), 0, 1);
        return true;
    }

    private void Advance(double nowMs)
    {
        if (nowMs < _at || nowMs > _until) { Reset(); return; }
        double dt = (nowMs - _at) / 1000;
        double acceleration = (_down ? _pull : -_fall) + _disturbance;
        _bar += _velocity * dt + acceleration * dt * dt / 2;
        _velocity += acceleration * dt; _fish += _fishVelocity * dt + _fishAcceleration * dt * dt / 2;
        _fishVelocity += _fishAcceleration * dt;
        if (_bar < _minimumBar || _bar > _maximumBar) { _bar = Math.Clamp(_bar, _minimumBar, _maximumBar); _velocity = 0; }
        if (_fish < _minimumFish || _fish > _maximumFish)
        { _fish = Math.Clamp(_fish, _minimumFish, _maximumFish); _fishVelocity = _fishAcceleration = 0; }
        _at = nowMs;
    }
}
