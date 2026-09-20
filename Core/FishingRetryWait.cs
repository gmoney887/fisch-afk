namespace FischMacroCS.Core;

/// <summary>Keep a fishing request alive without sending input while its context is unavailable.</summary>
public static class FishingRetryWait
{
    public static void Wait(IClock clock, Func<bool> ready, Action releaseInputs,
        Action reportWaiting, CancellationToken cancellation, int minimumDelayMs = 1000)
    {
        releaseInputs();
        long started = clock.Timestamp;
        int polls = 0;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            if (clock.ElapsedMilliseconds(started) >= minimumDelayMs && ready())
            {
                cancellation.ThrowIfCancellationRequested();
                return;
            }
            if (polls++ % 5 == 0) reportWaiting();
            // Poll context with no gameplay input. Stop cancels the wait immediately.
            clock.Delay(200, cancellation);
        }
    }
}
