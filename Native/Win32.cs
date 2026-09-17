using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FischMacroCS.Native;

public static partial class Win32
{
    public const int SRCCOPY = 0x00CC0020;
    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const int MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const int MOUSEEVENTF_LEFTUP = 0x0004;
    public const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const int MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    public const int MOD_NONE = 0x0000;
    public const int MOD_ALT = 0x0001;
    public const int MOD_CONTROL = 0x0002;
    public const int MOD_SHIFT = 0x0004;
    public const int WM_HOTKEY = 0x0312;

    public const int VK_F1 = 0x70;
    public const int VK_F2 = 0x71;
    public const int VK_F3 = 0x72;
    public const int VK_F4 = 0x73;
    public const int VK_F5 = 0x74;
    public const int VK_F6 = 0x75;
    public const int VK_F7 = 0x76;
    public const int VK_F8 = 0x77;
    public const int VK_F9 = 0x78;
    public const int VK_F10 = 0x79;
    public const int VK_F11 = 0x7A;
    public const int VK_F12 = 0x7B;
    public const int VK_INSERT = 0x2D;
    public const int VK_DELETE = 0x2E;
    public const int VK_HOME = 0x24;
    public const int VK_END = 0x23;
    public const int VK_PAUSE = 0x13;

    public static uint ParseVirtualKey(string keyName)
    {
        return keyName.Trim().ToUpperInvariant() switch
        {
            "F1" => 0x70,
            "F2" => 0x71,
            "F3" => 0x72,
            "F4" => 0x73,
            "F5" => 0x74,
            "F6" => 0x75,
            "F7" => 0x76,
            "F8" => 0x77,
            "F9" => 0x78,
            "F10" => 0x79,
            "F11" => 0x7A,
            "F12" => 0x7B,
            "INSERT" => 0x2D,
            "DELETE" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAUSE" => 0x13,
            _ => 0x75 // Default to F6
        };
    }

    public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (IntPtr)(-4);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public int bmiColors;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    public static void ForceSetForegroundWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        IntPtr fgWnd = GetForegroundWindow();
        if (fgWnd == hWnd) return;

        uint fgThread = GetWindowThreadProcessId(fgWnd, out _);
        uint curThread = GetCurrentThreadId();

        if (fgThread != 0 && fgThread != curThread)
        {
            AttachThreadInput(curThread, fgThread, true);
            SetForegroundWindow(hWnd);
            AttachThreadInput(curThread, fgThread, false);
        }
        else
        {
            SetForegroundWindow(hWnd);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    public const uint GA_PARENT = 1;
    public const uint GA_ROOT = 2;
    public const uint GA_ROOTOWNER = 3;

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    public static bool IsWindowOrChild(IntPtr child, IntPtr parent)
    {
        if (child == IntPtr.Zero || parent == IntPtr.Zero) return false;
        if (child == parent) return true;
        IntPtr root = GetAncestor(child, GA_ROOT);
        return root == parent;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth, int nHeight);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    public static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("winmm.dll")]
    public static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    public static extern uint timeEndPeriod(uint uPeriod);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKey(uint uCode, uint uMapType);
    public const uint MAPVK_VK_TO_VSC = 0;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumThreadWindows(uint dwThreadId, EnumWindowsProc lpfn, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    public const uint KEYEVENTF_KEYUP = 0x0002;

    // Window-targeted click messages (no global cursor movement)
    public const int WM_MOUSEMOVE   = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP   = 0x0202;
    public const int MK_LBUTTON     = 0x0001;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    public static IntPtr MakeLParam(int x, int y) => (IntPtr)((y << 16) | (x & 0xFFFF));

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;

    public const uint INPUT_MOUSE = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    /// <summary>
    /// Emits true relative mouse movement (no MOUSEEVENTF_ABSOLUTE).
    /// This generates genuine relative deltas (lLastX, lLastY) in Windows RAWMOUSE packets,
    /// triggering Roblox's UserInputService.InputChanged and GuiService button raycasting.
    /// </summary>
    public static void SendRelativeMove(int dx, int dy)
    {
        INPUT[] input = new INPUT[1];
        input[0].type = INPUT_MOUSE;
        input[0].mi.dx = dx;
        input[0].mi.dy = dy;
        input[0].mi.dwFlags = MOUSEEVENTF_MOVE; // Relative motion!
        SendInput(1, input, Marshal.SizeOf<INPUT>());
        mouse_event((int)MOUSEEVENTF_MOVE, dx, dy, 0, 0);
    }

    /// <summary>
    /// Safe coordinate mapping from client to screen without mutating input structs.
    /// </summary>
    public static bool SafeClientToScreen(IntPtr hwnd, int clientX, int clientY, out int screenX, out int screenY)
    {
        POINT pt = new POINT { X = clientX, Y = clientY };
        if (ClientToScreen(hwnd, ref pt))
        {
            screenX = pt.X;
            screenY = pt.Y;
            return true;
        }
        screenX = clientX;
        screenY = clientY;
        return false;
    }

    /// <summary>
    /// Strict coordinate boundary sanitizer. Ensures client coordinates are strictly within
    /// the target window's client bounds and maps safely to virtual desktop screen coordinates.
    /// </summary>
    public static bool SanitizeGameCoordinate(IntPtr hwnd, int clientX, int clientY, out int safeClientX, out int safeClientY, out int safeScreenX, out int safeScreenY, int inset = 5)
    {
        safeClientX = clientX;
        safeClientY = clientY;
        safeScreenX = clientX;
        safeScreenY = clientY;

        if (hwnd == IntPtr.Zero) return false;

        if (!GetClientRect(hwnd, out RECT rc) || rc.Width <= 0 || rc.Height <= 0)
        {
            return false;
        }

        int minX = inset;
        int maxX = Math.Max(inset, rc.Width - inset);
        int minY = inset;
        int maxY = Math.Max(inset, rc.Height - inset);

        int clampedX = Math.Clamp(clientX, minX, maxX);
        int clampedY = Math.Clamp(clientY, minY, maxY);

        if (clampedX != clientX || clampedY != clientY)
        {
            FischMacroCS.Core.SessionLogger.Instance.LogSafety(
                $"Coordinate clamped from ({clientX}, {clientY}) to ({clampedX}, {clampedY}) for window bounds {rc.Width}x{rc.Height}");
        }

        safeClientX = clampedX;
        safeClientY = clampedY;

        if (SafeClientToScreen(hwnd, clampedX, clampedY, out int sx, out int sy))
        {
            int vLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int vTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vWidth = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN));
            int vHeight = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN));

            safeScreenX = Math.Clamp(sx, vLeft, vLeft + vWidth - 1);
            safeScreenY = Math.Clamp(sy, vTop, vTop + vHeight - 1);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Send a high-reliability hardware-emulated click that satisfies DirectInput, RawInput,
    /// Win32 cursor positioning, relative motion listeners (Roblox GuiService raycaster),
    /// and direct window messaging.
    /// </summary>
    public static void SendHardwareClick(int screenX, int screenY, int clientX = -1, int clientY = -1, IntPtr targetHwnd = default)
    {
        // 0. Ensure target window has foreground focus so clicks are not consumed by window activation
        if (targetHwnd != IntPtr.Zero)
        {
            IntPtr fg = GetForegroundWindow();
            if (fg != targetHwnd && !IsWindowOrChild(fg, targetHwnd))
            {
                ForceSetForegroundWindow(targetHwnd);
                System.Threading.Thread.Sleep(15);
            }
        }

        // Clamp screen coordinates to virtual desktop bounds to prevent off-screen throws
        int vLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vWidth = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN));
        int vHeight = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN));

        screenX = Math.Clamp(screenX, vLeft, vLeft + vWidth - 1);
        screenY = Math.Clamp(screenY, vTop, vTop + vHeight - 1);

        FischMacroCS.Core.SessionLogger.Instance.LogInput("HardwareClick", screenX, screenY, clientX, clientY);

        // 1. Position OS Cursor
        SetCursorPos(screenX, screenY);

        int normX = (int)Math.Round(((screenX - vLeft) * 65535.0) / (vWidth - 1));
        int normY = (int)Math.Round(((screenY - vTop) * 65535.0) / (vHeight - 1));

        // Dispatch absolute hardware move so Roblox's RawInput queue updates internal mouse position
        INPUT[] moveInputs = new INPUT[1];
        moveInputs[0].type = INPUT_MOUSE;
        moveInputs[0].mi.dx = normX;
        moveInputs[0].mi.dy = normY;
        moveInputs[0].mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
        SendInput(1, moveInputs, Marshal.SizeOf<INPUT>());

        // Post WM_MOUSEMOVE directly to the window message queue
        if (targetHwnd != IntPtr.Zero && clientX >= 0 && clientY >= 0)
        {
            PostMessage(targetHwnd, WM_MOUSEMOVE, IntPtr.Zero, MakeLParam(clientX, clientY));
        }

        // 3. Relative Hardware Wiggle:
        // Roblox GuiService button hover detection (MouseEnter/MouseMoved) does NOT update on pure absolute moves.
        // It requires relative mouse movement (MOUSE_MOVE_RELATIVE). We send micro-wiggles back and forth (+3,-3, etc.)
        // so Roblox samples relative motion across frames and locks hover onto the Shake button.
        SendRelativeMove(3, 2);
        System.Threading.Thread.Sleep(10);
        SendRelativeMove(-3, -2);
        System.Threading.Thread.Sleep(10);
        SendRelativeMove(-2, 2);
        System.Threading.Thread.Sleep(10);
        SendRelativeMove(2, -2);
        System.Threading.Thread.Sleep(15);

        // Ensure cursor is locked on the exact target screen point
        SetCursorPos(screenX, screenY);

        // 4. Mouse Down: Hardware SendInput + mouse_event + Window Message
        INPUT[] downInputs = new INPUT[1];
        downInputs[0].type = INPUT_MOUSE;
        downInputs[0].mi.dx = normX;
        downInputs[0].mi.dy = normY;
        downInputs[0].mi.dwFlags = MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
        SendInput(1, downInputs, Marshal.SizeOf<INPUT>());
        mouse_event((int)MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);

        if (targetHwnd != IntPtr.Zero && clientX >= 0 && clientY >= 0)
        {
            PostMessage(targetHwnd, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, MakeLParam(clientX, clientY));
        }

        // Hold down while performing a micro-wiggle so GuiButton.InputBegan registers
        System.Threading.Thread.Sleep(20);
        SendRelativeMove(1, 0);
        System.Threading.Thread.Sleep(10);
        SendRelativeMove(-1, 0);
        System.Threading.Thread.Sleep(15);

        // 5. Mouse Up: Hardware SendInput + mouse_event + Window Message
        INPUT[] upInputs = new INPUT[1];
        upInputs[0].type = INPUT_MOUSE;
        upInputs[0].mi.dx = normX;
        upInputs[0].mi.dy = normY;
        upInputs[0].mi.dwFlags = MOUSEEVENTF_LEFTUP | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
        SendInput(1, upInputs, Marshal.SizeOf<INPUT>());
        mouse_event((int)MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);

        if (targetHwnd != IntPtr.Zero && clientX >= 0 && clientY >= 0)
        {
            PostMessage(targetHwnd, WM_LBUTTONUP, IntPtr.Zero, MakeLParam(clientX, clientY));
        }

        // Re-center cursor
        SetCursorPos(screenX, screenY);
    }

    /// <summary>
    /// Send a left click directly to a specific window at client coordinates (x,y)
    /// without moving the global cursor. This prevents accidental clicks on overlapping windows.
    /// </summary>
    public static void SendClickToWindow(IntPtr hWnd, int clientX, int clientY)
    {
        IntPtr lParam = MakeLParam(clientX, clientY);
        IntPtr wParam = (IntPtr)MK_LBUTTON;
        SendMessage(hWnd, WM_LBUTTONDOWN, wParam, lParam);
        System.Threading.Thread.Sleep(15);
        SendMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, lParam);
    }

    public static void SendKeyPress(char c)
    {
        byte vk = 0;
        if (c >= '1' && c <= '9')
            vk = (byte)(0x30 + (c - '0'));
        else if (c == '0')
            vk = 0x30;
        else if (c >= 'a' && c <= 'z')
            vk = (byte)(0x41 + (c - 'a'));
        else if (c >= 'A' && c <= 'Z')
            vk = (byte)(0x41 + (c - 'A'));
        else if (c == '\r' || c == '\n')
            vk = 0x0D; // VK_RETURN
        else if (c == '\b')
            vk = 0x08; // VK_BACK
        else if (c == ' ')
            vk = 0x20; // VK_SPACE
        else if (c == 27)
            vk = 0x1B; // VK_ESCAPE
        else if (c == '`' || c == '~')
            vk = 0xC0; // VK_OEM_3
        
        if (vk != 0)
        {
            byte scan = (byte)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
            keybd_event(vk, scan, 0, 0);
            System.Threading.Thread.Sleep(45);
            keybd_event(vk, scan, KEYEVENTF_KEYUP, 0);
        }
    }

    public static void SendKeyString(string text, int delayMs = 40)
    {
        foreach (char ch in text)
        {
            SendKeyPress(ch);
            System.Threading.Thread.Sleep(delayMs);
        }
    }

    public static void SelectAllAndClear()
    {
        byte vkCtrl = 0x11;
        byte scanCtrl = (byte)MapVirtualKey(vkCtrl, MAPVK_VK_TO_VSC);
        byte vkA = 0x41;
        byte scanA = (byte)MapVirtualKey(vkA, MAPVK_VK_TO_VSC);

        keybd_event(vkCtrl, scanCtrl, 0, 0);
        System.Threading.Thread.Sleep(25);
        keybd_event(vkA, scanA, 0, 0);
        System.Threading.Thread.Sleep(35);
        keybd_event(vkA, scanA, KEYEVENTF_KEYUP, 0);
        keybd_event(vkCtrl, scanCtrl, KEYEVENTF_KEYUP, 0);
        System.Threading.Thread.Sleep(45);
        SendKeyPress('\b');
    }

    public static IntPtr FindRobloxWindow()
    {
        string logFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "find_roblox.log");
        void Log(string msg) { try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); } catch { } }

        // 1. Try standard class name
        IntPtr hwnd = FindWindow("WINDOWSCLIENT", null);
        Log($"Step 1 FindWindow(WINDOWSCLIENT): {hwnd}, Vis={IsWindowVisible(hwnd)}");
        if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd))
            return hwnd;

        // 2. Try window title
        hwnd = FindWindow(null, "Roblox");
        Log($"Step 2 FindWindow(Roblox): {hwnd}, Vis={IsWindowVisible(hwnd)}");
        if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd))
            return hwnd;

        hwnd = FindWindow(null, "Roblox Player");
        Log($"Step 2b FindWindow(Roblox Player): {hwnd}, Vis={IsWindowVisible(hwnd)}");
        if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd))
            return hwnd;

        // 3. Check processes
        try
        {
            var procs = Process.GetProcessesByName("RobloxPlayerBeta");
            Log($"Step 3 procs count: {procs.Length}");
            foreach (var p in procs)
            {
                Log($"Proc {p.Id}: MainHwnd={p.MainWindowHandle}, Vis={IsWindowVisible(p.MainWindowHandle)}");
                if (p.MainWindowHandle != IntPtr.Zero && IsWindowVisible(p.MainWindowHandle))
                    return p.MainWindowHandle;
            }
        }
        catch (Exception ex) { Log($"Step 3 ex: {ex.Message}"); }

        // 3b. Check thread windows of RobloxPlayerBeta processes
        IntPtr candidate = IntPtr.Zero;
        try
        {
            var procs = Process.GetProcessesByName("RobloxPlayerBeta");
            foreach (var p in procs)
            {
                foreach (ProcessThread t in p.Threads)
                {
                    EnumThreadWindows((uint)t.Id, (h, _) =>
                    {
                        var sbTitle = new StringBuilder(256);
                        GetWindowText(h, sbTitle, 256);
                        var sbClass = new StringBuilder(256);
                        GetClassName(h, sbClass, 256);
                        bool vis = IsWindowVisible(h);
                        Log($"ThreadWindow on Thread {t.Id}: HWND={h}, Class='{sbClass}', Title='{sbTitle}', Vis={vis}");
                        if (candidate == IntPtr.Zero && (sbClass.ToString() == "WINDOWSCLIENT" || sbTitle.ToString().Contains("Roblox") || vis))
                        {
                            candidate = h;
                        }
                        return true;
                    }, IntPtr.Zero);
                }
            }
            if (candidate != IntPtr.Zero)
            {
                Log($"Found candidate from thread windows: {candidate}");
                return candidate;
            }
        }
        catch (Exception ex) { Log($"Step 3b ex: {ex.Message}"); }

        // 4. Fallback enumeration with process verification
        try
        {
            EnumWindows((h, _) =>
            {
                if (!IsWindowVisible(h)) return true;

                GetWindowThreadProcessId(h, out uint pid);
                string procName = "";
                try { procName = Process.GetProcessById((int)pid).ProcessName; } catch { }

                if (!procName.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase))
                    return true;

                var sbTitle = new StringBuilder(256);
                GetWindowText(h, sbTitle, 256);
                string title = sbTitle.ToString();

                var sbClass = new StringBuilder(256);
                GetClassName(h, sbClass, 256);
                string cls = sbClass.ToString();

                Log($"Enum candidate: HWND={h}, PID={pid}, Class='{cls}', Title='{title}'");

                if (title.Equals("Roblox", StringComparison.OrdinalIgnoreCase) ||
                    cls.Equals("WINDOWSCLIENT", StringComparison.OrdinalIgnoreCase))
                {
                    candidate = h;
                    return false; // Stop enumeration
                }

                if (title.Contains("Roblox", StringComparison.OrdinalIgnoreCase))
                {
                    candidate = h;
                }
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) { Log($"Step 4 ex: {ex.Message}"); }

        Log($"Final candidate: {candidate}");
        return candidate;
    }

    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const int SW_RESTORE = 9;
    public const uint SPI_GETWORKAREA = 0x0030;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out RECT pvParam, uint fWinIni);

    public static bool SnapWindowsSideBySide(IntPtr robloxHwnd, IntPtr macroHwnd)
    {
        if (robloxHwnd == IntPtr.Zero || macroHwnd == IntPtr.Zero)
            return false;

        // Restore window if maximized or minimized so it can be cleanly resized
        ShowWindow(robloxHwnd, SW_RESTORE);

        if (!SystemParametersInfo(SPI_GETWORKAREA, 0, out RECT rcWork, 0))
            return false;

        int totalW = rcWork.Width;
        int totalH = rcWork.Height;
        if (totalW <= 0 || totalH <= 0) return false;

        // On wide / ultrawide monitors (aspect ratio >= 16:9), split screen side-by-side:
        // Roblox on the left: ~66% width
        // Macro on the right: remaining ~34% width
        int robloxW = (int)(totalW * 0.66);
        int macroW = totalW - robloxW;

        // Position Roblox on left
        SetWindowPos(robloxHwnd, IntPtr.Zero, rcWork.Left, rcWork.Top, robloxW, totalH, SWP_NOZORDER | SWP_SHOWWINDOW);

        // Position Macro on right
        SetWindowPos(macroHwnd, IntPtr.Zero, rcWork.Left + robloxW, rcWork.Top, macroW, totalH, SWP_NOZORDER | SWP_SHOWWINDOW);

        return true;
    }

    public static bool DockFullScreenAndTopRight(IntPtr robloxHwnd, IntPtr macroHwnd, int targetW = 465, int targetH = 585)
    {
        if (robloxHwnd == IntPtr.Zero || macroHwnd == IntPtr.Zero)
            return false;

        ShowWindow(robloxHwnd, SW_RESTORE);

        if (!SystemParametersInfo(SPI_GETWORKAREA, 0, out RECT rcWork, 0))
            return false;

        // 1. Expand Roblox to fill the full desktop work area
        SetWindowPos(robloxHwnd, IntPtr.Zero, rcWork.Left, rcWork.Top, rcWork.Width, rcWork.Height, SWP_NOZORDER | SWP_SHOWWINDOW);

        // 2. Dock Angler neatly in the top right corner as a comfortable, readable floating window
        int macroW = Math.Clamp(targetW, 380, (int)(rcWork.Width * 0.40));
        int macroH = Math.Clamp(targetH, 500, (int)(rcWork.Height * 0.90));
        int macroX = rcWork.Right - macroW;
        int macroY = rcWork.Top;

        SetWindowPos(macroHwnd, (IntPtr)(-1) /* HWND_TOPMOST */, macroX, macroY, macroW, macroH, SWP_SHOWWINDOW);

        return true;
    }
}
