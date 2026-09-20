using FischMacroCS.Native;

namespace FischMacroCS.Core;

/// <summary>Windows boundary for the live engine. Replays override this without touching the desktop.</summary>
public class GameDesktop
{
    public virtual IntPtr FindRobloxWindow() => Win32.FindRobloxWindow();
    public virtual bool IsIconic(IntPtr window) => Win32.IsIconic(window);
    public virtual IntPtr GetForegroundWindow() => Win32.GetForegroundWindow();
    public virtual bool GetClientRect(IntPtr window, out Win32.RECT rect) => Win32.GetClientRect(window, out rect);
    public virtual uint GetDpiForWindow(IntPtr window) => Win32.GetDpiForWindow(window);
    public virtual void ForceSetForegroundWindow(IntPtr window) => Win32.ForceSetForegroundWindow(window);
    public virtual void ShowWindowAsync(IntPtr window, int command) => Win32.ShowWindowAsync(window, command);
    public virtual bool ClientToScreen(IntPtr window, ref Win32.POINT point) => Win32.ClientToScreen(window, ref point);
    public virtual bool ScreenToClient(IntPtr window, ref Win32.POINT point) => Win32.ScreenToClient(window, ref point);
    public virtual bool GetCursorPos(out Win32.POINT point) => Win32.GetCursorPos(out point);
    public virtual IntPtr WindowFromPoint(Win32.POINT point) => Win32.WindowFromPoint(point);
    public virtual void GetWindowThreadProcessId(IntPtr window, out uint process) => Win32.GetWindowThreadProcessId(window, out process);
    public virtual bool SanitizeGameCoordinate(IntPtr window, int x, int y, out int cx, out int cy, out int sx, out int sy) =>
        Win32.SanitizeGameCoordinate(window, x, y, out cx, out cy, out sx, out sy);
    public virtual void timeBeginPeriod(uint period) => Win32.timeBeginPeriod(period);
    public virtual void timeEndPeriod(uint period) => Win32.timeEndPeriod(period);
}
