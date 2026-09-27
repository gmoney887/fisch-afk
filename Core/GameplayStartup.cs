using FischMacroCS.Native;

namespace FischMacroCS.Core;

public static class GameplayStartup
{
    // Restoring a minimized window and foreground activation are asynchronous.
    // This grace period is only for startup; input-time focus checks remain strict.
    public static (IntPtr Window, Win32.RECT Geometry, uint Dpi) Acquire(
        GameDesktop desktop, IClock clock, CancellationToken cancellation, Action checkStopped)
    {
        var window = desktop.FindRobloxWindow();
        if (window == IntPtr.Zero)
            throw new GameplayInterruptedException("Roblox window was not found. Launch Roblox and join Fisch first.");
        for (int attempt = 0; attempt < 30; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            checkStopped();
            if (attempt % 5 == 0)
            {
                if (desktop.IsIconic(window)) desktop.ShowWindowAsync(window, Win32.SW_RESTORE);
                desktop.ForceSetForegroundWindow(window);
            }
            if (!desktop.IsIconic(window) && desktop.GetForegroundWindow() == window &&
                desktop.GetClientRect(window, out var rect) && rect.Width > 0 && rect.Height > 0)
                return (window, rect, desktop.GetDpiForWindow(window));
            clock.Delay(50, cancellation);
        }
        throw new GameplayInterruptedException("Roblox could not become active. Restore its window and try again.");
    }
}
