using FischMacroCS.Vision;

namespace FischMacroCS.Core;

/// <summary>A changed camera is a suspicion, not proof that fishing stopped working.</summary>
public sealed class FishingViewSafety(IClock clock)
{
    private long? _suspectedAt;
    private long? _lastLiveReelAt;
    private long? _lastConfirmedCatchAt;
    private long _nextCycleBudgetMs;
    public bool IsSuspected => _suspectedAt.HasValue;
    public bool HasLiveReel { get; private set; }
    public bool HasRecentProgress => _lastConfirmedCatchAt.HasValue &&
        clock.ElapsedMilliseconds(_lastConfirmedCatchAt.Value) < _nextCycleBudgetMs;

    public bool Observe(FishingViewStatus status, bool liveReel, int reelTimeoutMs)
    {
        if (status is FishingViewStatus.Stable or FishingViewStatus.Learning)
        {
            ClearSuspicion();
            return false;
        }
        _suspectedAt ??= clock.Timestamp;
        HasLiveReel = liveReel;
        if (liveReel) _lastLiveReelAt = clock.Timestamp;
        double elapsed = clock.ElapsedMilliseconds(_suspectedAt.Value);
        // Give the current catch time to finalize. Do not interrupt its controller
        // with repeated mouse releases or a blocking scene-only polling loop.
        bool recentReel = _lastLiveReelAt.HasValue && clock.ElapsedMilliseconds(_lastLiveReelAt.Value) < 2000;
        return !HasRecentProgress && status == FishingViewStatus.Changed && elapsed >= 3000 &&
            (!recentReel || elapsed >= Math.Max(3000, reelTimeoutMs));
    }

    public bool ConfirmCatch(long nextCycleBudgetMs = 0)
    {
        bool revalidate = IsSuspected;
        ClearSuspicion();
        // A successful catch authorizes a bounded next cycle even if its camera
        // animation changes the scene after the catch was finalized. Only another
        // confirmed catch renews this deadline; stable frames/reel UI do not.
        _lastConfirmedCatchAt = clock.Timestamp;
        _nextCycleBudgetMs = Math.Max(0, nextCycleBudgetMs);
        return revalidate;
    }

    private void ClearSuspicion() { _suspectedAt = _lastLiveReelAt = null; HasLiveReel = false; }
    public void Reset()
    {
        ClearSuspicion();
        _lastConfirmedCatchAt = null;
        _nextCycleBudgetMs = 0;
    }
}
