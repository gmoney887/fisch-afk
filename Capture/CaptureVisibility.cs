using FischMacroCS.Native;
using OpenCvSharp;
using System.Runtime.InteropServices;

namespace FischMacroCS.Capture;

/// <summary>Reject desktop pixels covered by a different top-level window.</summary>
public static class CaptureVisibility
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    // A click-through layered window can have a desktop-sized transparent canvas
    // around a tiny cursor/adornment. Its bounding box is not proof of occlusion.
    public static bool HasOpaqueBounds(int extendedStyle) =>
        (extendedStyle & (0x00080000 | 0x00000020)) != (0x00080000 | 0x00000020);

    public static bool Overlaps(Rect capture, Rect window) =>
        capture.Width > 0 && capture.Height > 0 && window.Width > 0 && window.Height > 0
        && capture.Left < window.Right && window.Left < capture.Right
        && capture.Top < window.Bottom && window.Top < capture.Bottom;

    public static bool IsUnobscured(IntPtr target, Rect screenRegion) => IsUnobscured(target, screenRegion, out _);

    public static bool IsUnobscured(IntPtr target, Rect screenRegion, out string blocker)
    {
        // Store clients may expose a child surface; EnumWindows only lists roots.
        IntPtr root = Win32.GetAncestor(target, Win32.GA_ROOT);
        if (root != IntPtr.Zero) target = root;
        bool found = false, covered = false;
        string description = "Roblox window is no longer in the desktop window list";
        Win32.EnumWindows((window, _) =>
        {
            if (window == target) { found = true; return false; }
            if (!Win32.IsWindowVisible(window) || Win32.IsIconic(window)) return true;
            if (!HasOpaqueBounds(GetWindowLong(window, -20))) return true;
            if (DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            if (Win32.GetWindowRect(window, out var bounds) &&
                Overlaps(screenRegion, new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height)))
            {
                var name = new System.Text.StringBuilder(256);
                Win32.GetClassName(window, name, name.Capacity);
                description = $"{name} (window {window})";
                covered = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        blocker = description;
        return found && !covered;
    }
}
