using FischMacroCS.Vision;

namespace FischMacroCS.Core;

/// <summary>Confirm a live reel before allowing cast failure recovery to send inputs.</summary>
public static class ReelEntryGuard
{
    public static bool Confirm(Func<DetectionResult> observe, Action<int> delay, CancellationToken cancellation)
    {
        for (int sample = 0; sample < 2; sample++)
        {
            cancellation.ThrowIfCancellationRequested();
            var detection = observe();
            if (!detection.BarFound || !detection.FishFound) return false;
            if (sample == 0) delay(30);
        }
        cancellation.ThrowIfCancellationRequested();
        return true;
    }
}
