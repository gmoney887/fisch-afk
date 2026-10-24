namespace FischMacroCS.Core;

/// <summary>Input-aware kinematics. Estimates never replace image coordinates.</summary>
public sealed class ReelInputMotion
{
    private readonly List<(double At, bool Held)> _edges = new();
    public void Reset() => _edges.Clear();
    public void Input(double at, bool held)
    {
        if (_edges.Count > 0 && at < _edges[^1].At) Reset();
        if (_edges.Count == 0 || _edges[^1].Held != held) _edges.Add((at,held));
        while (_edges.Count > 2 && _edges[1].At < at-1000) _edges.RemoveAt(0);
    }
    public (double Displacement, double Velocity) Integrate(double from, double to, double pull, double fall)
    {
        if (to <= from) return (0,0);
        bool held=false; double at=from,position=0,velocity=0;
        foreach (var edge in _edges)
        {
            if (edge.At <= from) { held=edge.Held; continue; }
            if (edge.At >= to) break;
            Step(edge.At); held=edge.Held;
        }
        Step(to); return (position,velocity);
        void Step(double until)
        {
            double dt=(until-at)/1000, acceleration=held?pull:-fall;
            position+=velocity*dt+acceleration*dt*dt/2;velocity+=acceleration*dt;at=until;
        }
    }
    public double CaptureVelocity(double from, double to, double previous, double current, double pull, double fall)
    {
        double dt=(to-from)/1000;
        if (dt<=0 || dt>.25) return 0;
        var motion=Integrate(from,to,pull,fall);
        return (current-previous-motion.Displacement)/dt+motion.Velocity;
    }
    public (double Position, double Velocity) Project(double from, double to, double position, double velocity, double pull, double fall)
    {
        if(to<from || to-from>250)return(position,velocity);
        var motion=Integrate(from,to,pull,fall);
        return(position+velocity*(to-from)/1000+motion.Displacement,velocity+motion.Velocity);
    }
}
