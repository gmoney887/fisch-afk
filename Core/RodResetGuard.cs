namespace FischMacroCS.Core;

/// <summary>Require a visible deselection before a second toggle can count as a rod reset.</summary>
public static class RodResetGuard
{
    public static void Unequip(Action toggle, Func<(bool Equipped, bool Visible)> observe,
        Action<int> delay, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        toggle();
        int confirmed = 0;
        for (int sample = 0; sample < 10; sample++)
        {
            delay(100);
            cancellation.ThrowIfCancellationRequested();
            var state = observe();
            if (!state.Visible)
                { confirmed = 0; continue; }
            confirmed = state.Equipped ? 0 : confirmed + 1;
            if (confirmed >= 2) return;
        }
        throw new GameplayInterruptedException("Rod unequip was not visually confirmed; reset was not completed.");
    }
}
