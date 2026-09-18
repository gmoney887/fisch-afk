using System.IO;
using System.Text.Json;

namespace FischMacroCS.Core;

public sealed record AdaptationProfile(int CompatibilityVersion, string Rod, int Width, int Height,
    double PullPerTrackSecondSquared, int Samples, int ConfirmedCatches, DateTime ValidatedUtc,
    double ReleasePerTrackSecondSquared = 0, int ReleaseSamples = 0);

/// <summary>Small bounded candidate updates; only confirmed catches can persist a compatible profile.</summary>
public sealed class BoundedRodEstimator
{
    private readonly double _baseline;
    private int _rejected;
    public double Pull { get; private set; }
    public int Samples { get; private set; }
    public int ConfirmedCatches { get; private set; }
    private readonly double _validated;
    public BoundedRodEstimator(double baseline, double? validated = null)
    {
        if (!double.IsFinite(baseline) || baseline <= 0) throw new ArgumentOutOfRangeException(nameof(baseline));
        _baseline = baseline;
        _validated = validated is double value && double.IsFinite(value)
            ? Math.Clamp(value, baseline * .75, baseline * 1.25) : baseline;
        Pull = _validated;
    }
    public bool Observe(double acceleration, double elapsedSeconds, bool confident, bool fresh)
    {
        if (!confident || !fresh || !double.IsFinite(elapsedSeconds) || elapsedSeconds < .005 || elapsedSeconds > .1 ||
            !double.IsFinite(acceleration) || acceleration < _baseline * .5 || acceleration > _baseline * 2)
        {
            if (++_rejected >= 6) { Pull = _validated; Samples = ConfirmedCatches = 0; }
            return false;
        }
        _rejected = 0;
        double next = Pull * .98 + acceleration * .02;
        Pull = Math.Clamp(next, _baseline * .75, _baseline * 1.25);
        Samples++;
        return true;
    }
    public void ConfirmCatch() => ConfirmedCatches++;
    public bool CanPersist => Samples >= 30 && ConfirmedCatches >= 3;
    public static AdaptationProfile? Load(string path, string rod, int width, int height)
    {
        try
        {
            var p = JsonSerializer.Deserialize<AdaptationProfile>(File.ReadAllText(path));
            return p is { CompatibilityVersion: 1, Samples: >= 30, ConfirmedCatches: >= 3 } && p.Rod == rod &&
                p.Width == width && p.Height == height && double.IsFinite(p.PullPerTrackSecondSquared) &&
                double.IsFinite(p.ReleasePerTrackSecondSquared) && p.ReleasePerTrackSecondSquared >= 0 &&
                p.PullPerTrackSecondSquared > 0 && p.ValidatedUtc <= DateTime.UtcNow &&
                p.ValidatedUtc > DateTime.UtcNow.AddDays(-7) ? p : null;
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
