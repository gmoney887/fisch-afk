using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using FischMacroCS.Native;
using FischMacroCS.Core;

namespace FischMacroCS.Capture;

/// <summary>Only our explicitly registered dashboard may be absent from desktop capture.</summary>
public static class CaptureExclusion
{
    private static readonly ConcurrentDictionary<IntPtr, byte> Registered = new();
    private static readonly ConcurrentDictionary<IntPtr, bool> Yielded = new();
    private static readonly ConcurrentDictionary<IntPtr, bool> PinPreference = new();
    private static readonly object ZOrderGate = new();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    public static bool Register(IntPtr window)
    {
        // Older Windows interprets 0x11 as a black rectangle, not transparent exclusion.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) || window == IntPtr.Zero ||
            !SetWindowDisplayAffinity(window, 0x11)) return false;
        Registered[window] = 0;
        return IsExcluded(window);
    }
    public static bool IsExcluded(IntPtr window) => Registered.ContainsKey(window) &&
        GetWindowDisplayAffinity(window, out uint affinity) && CanIgnore(true, affinity);
    public static bool CanIgnore(bool registered, uint affinity) => registered && affinity == 0x11;
    public static void Unregister(IntPtr window)
    {
        Registered.TryRemove(window, out _);
        PinPreference.TryRemove(window, out _);
        Yielded.TryRemove(window, out _);
    }

    public static bool SetPinned(IntPtr window, bool pinned)
    {
        if (window == IntPtr.Zero) return false;
        PinPreference[window] = pinned;
        // Do not block the UI while a worker's SetWindowPos needs its message
        // pump, and never repin in the middle of yielding a game click.
        if (!System.Threading.Monitor.TryEnter(ZOrderGate)) return true;
        try
        {
            if (Yielded.ContainsKey(window)) return true;
            return Win32.SetWindowPos(window, pinned ? (IntPtr)(-1) : (IntPtr)(-2), 0, 0, 0, 0,
                0x1 | 0x2 | Win32.SWP_NOACTIVATE);
        }
        finally { System.Threading.Monitor.Exit(ZOrderGate); }
    }

    public static void PrepareGamePress(IntPtr target, int screenX, int screenY)
    {
        lock (ZOrderGate)
        {
        if (target == IntPtr.Zero) return;
        var point = new Win32.POINT { X = screenX, Y = screenY };
        IntPtr under = Win32.WindowFromPoint(point);
        IntPtr root = Win32.GetAncestor(under, Win32.GA_ROOT);
        if (IsExcluded(root))
        {
            // Precise UI clicks may need this brief yield. Fishing holds choose
            // an uncovered game point before pressing, keeping the dashboard pinned.
            Yielded.TryAdd(root, (GetWindowLong(root, -20) & 0x8) != 0);
            if (!Win32.SetWindowPos(root, (IntPtr)1, 0, 0, 0, 0, 0x1 | 0x2 | Win32.SWP_NOACTIVATE))
            {
                RestoreAfterPress();
                throw new GameplayInterruptedException("Unable to yield dashboard for the game click.");
            }
            under = Win32.WindowFromPoint(point);
        }
        if (under != target && !Win32.IsWindowOrChild(under, target))
        {
            RestoreAfterPress();
            throw new GameplayInterruptedException("Another window covers the game click target.");
        }
        }
    }
    public static void RestoreAfterPress()
    {
        lock (ZOrderGate)
        {
        foreach (var pair in Yielded)
            if (Yielded.TryRemove(pair.Key, out bool topmost) && Registered.ContainsKey(pair.Key))
            {
                Win32.SetWindowPos(pair.Key, (PinPreference.TryGetValue(pair.Key, out bool pinned) ? pinned : topmost)
                    ? (IntPtr)(-1) : (IntPtr)(-2), 0, 0, 0, 0,
                    0x1 | 0x2 | Win32.SWP_NOACTIVATE);
            }
        }
    }
}
