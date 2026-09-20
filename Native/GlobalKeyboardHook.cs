using System;
using System.Runtime.InteropServices;

namespace FischMacroCS.Native;

public class GlobalKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;
    private readonly Core.KeyEdgeTracker _edges = new();

    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(IntPtr lpModuleName);

    private readonly HookProc _hookProc;
    private IntPtr _hookId = IntPtr.Zero;

    public uint ToggleVk { get; set; } = 0x75; // F6
    public uint ReEquipVk { get; set; } = 0x76; // F7
    public uint StopVk { get; set; } = 0x23; // End

    public event Action? OnToggle;
    public event Action? OnReEquip;
    public event Action? OnStop;

    public bool IsInstalled => _hookId != IntPtr.Zero;

    public GlobalKeyboardHook()
    {
        // Keep GC reference alive so the callback delegate is not collected
        _hookProc = HookCallback;
        Install();
    }

    public void Install()
    {
        if (_hookId != IntPtr.Zero) return;

        IntPtr hMod = GetModuleHandle(IntPtr.Zero);
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, hMod, 0);
    }

    public void Uninstall()
    {
        _edges.Reset();
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN
            || wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP))
        {
            int vkCode = Marshal.ReadInt32(lParam);
            bool down = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
            bool injected = (Marshal.ReadInt32(lParam, 8) & 0x10) != 0;
            if (!_edges.Observe(vkCode, down, injected))
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            if (vkCode == ToggleVk)
            {
                OnToggle?.Invoke();
            }
            else if (vkCode == ReEquipVk)
            {
                OnReEquip?.Invoke();
            }
            else if (vkCode == StopVk)
            {
                OnStop?.Invoke();
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Uninstall();
    }
}
