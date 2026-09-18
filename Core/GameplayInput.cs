using FischMacroCS.Native;

namespace FischMacroCS.Core;

public interface IInputSink
{
    void SendHardwareMouseDown(int sx = -1, int sy = -1, int cx = -1, int cy = -1, IntPtr window = default);
    void SendHardwareMouseUp(int sx = -1, int sy = -1, int cx = -1, int cy = -1, IntPtr window = default);
    void SendHardwareMouseMove(int sx, int sy, int cx = -1, int cy = -1, IntPtr window = default);
    void SendHardwareClick(int sx, int sy, int cx = -1, int cy = -1, IntPtr window = default);
    void SendRelativeMove(int x, int y);
    void SetCursorPos(int x, int y);
    void SendKeyPress(char key);
    void SendKeyString(string text, int delayMs = 40);
    void SelectAllAndClear();
    void mouse_event(int flags, int x, int y, int data, int extra);
    void keybd_event(byte key, byte scan, uint flags, int extra);
    void ReleaseAll();
}

/// <summary>Every input edge checks ownership, cancellation, focus and geometry.</summary>
public sealed class GameplayInput(Action validate, Action<int> delay, Action<string>? record = null, IInputSink? hardware = null) : IInputSink
{
    private readonly IInputSink _hardware = hardware ?? new Win32InputSink();
    private readonly object _gate = new();
    private readonly HashSet<byte> _keys = new();
    private bool _left;
    private bool _right;
    private (int X, int Y, IntPtr Window) _leftTarget;
    private void Send(string name, Action action)
    {
        lock (_gate)
        {
            try { validate(); action(); record?.Invoke(name); }
            catch { ReleaseAll(); throw; }
        }
    }
    public void SendHardwareMouseDown(int sx = -1, int sy = -1, int cx = -1, int cy = -1, IntPtr window = default) =>
        Send($"MouseDown:{cx},{cy}", () => { _leftTarget = (cx, cy, window); _left = true; _hardware.SendHardwareMouseDown(sx, sy, cx, cy, window); });
    public void SendHardwareMouseUp(int sx = -1, int sy = -1, int cx = -1, int cy = -1, IntPtr window = default)
    {
        lock (_gate)
        {
            if (_left) _hardware.SendHardwareMouseUp(cx: _leftTarget.X, cy: _leftTarget.Y, window: _leftTarget.Window);
            _left = false; record?.Invoke("MouseUp");
        }
    }
    public void SendHardwareMouseMove(int sx, int sy, int cx = -1, int cy = -1, IntPtr window = default) =>
        Send("MouseMove", () => _hardware.SendHardwareMouseMove(sx, sy, cx, cy, window));
    public void SendHardwareClick(int sx, int sy, int cx = -1, int cy = -1, IntPtr window = default)
    {
        SendHardwareMouseMove(sx, sy, cx, cy, window);
        SendHardwareMouseDown(sx, sy, cx, cy, window);
        try { delay(45); } finally { SendHardwareMouseUp(); }
    }
    public void SetCursorPos(int x, int y) => Send("CursorMove", () => _hardware.SetCursorPos(x, y));
    public void SendRelativeMove(int x, int y) => Send("RelativeMove", () => _hardware.SendRelativeMove(x, y));
    public void mouse_event(int flags, int x, int y, int data, int extra) => Send("MouseEvent", () =>
    {
        if ((flags & (int)Win32.MOUSEEVENTF_RIGHTDOWN) != 0) _right = true;
        if ((flags & (int)Win32.MOUSEEVENTF_RIGHTUP) != 0) _right = false;
        _hardware.mouse_event(flags, x, y, data, extra);
    });
    public void keybd_event(byte key, byte scan, uint flags, int extra)
    {
        if ((flags & Win32.KEYEVENTF_KEYUP) != 0)
        {
            lock (_gate) { _hardware.keybd_event(key, scan, flags, extra); _keys.Remove(key); record?.Invoke($"KeyUp:{key}"); }
        }
        else Send($"KeyDown:{key}", () => { _keys.Add(key); _hardware.keybd_event(key, scan, flags, extra); });
    }
    public void SendKeyPress(char key)
    {
        byte vk = key switch { '\r' or '\n' => 13, '\b' => 8, (char)27 => 27, '`' or '~' => 0xC0, _ => (byte)char.ToUpperInvariant(key) };
        keybd_event(vk, 0, 0, 0);
        try { delay(45); } finally { keybd_event(vk, 0, Win32.KEYEVENTF_KEYUP, 0); }
    }
    public void SendKeyString(string text, int delayMs = 40)
    {
        foreach (char key in text) { SendKeyPress(key); delay(delayMs); }
    }
    public void SelectAllAndClear()
    {
        keybd_event(0x11, 0, 0, 0);
        try { SendKeyPress('a'); } finally { keybd_event(0x11, 0, Win32.KEYEVENTF_KEYUP, 0); }
        SendKeyPress('\b');
    }
    public void ReleaseAll()
    {
        lock (_gate)
        {
            if (_left) _hardware.SendHardwareMouseUp(cx: _leftTarget.X, cy: _leftTarget.Y, window: _leftTarget.Window);
            if (_right) _hardware.mouse_event((int)Win32.MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
            foreach (byte key in _keys) _hardware.keybd_event(key, 0, Win32.KEYEVENTF_KEYUP, 0);
            _keys.Clear(); _left = _right = false;
            record?.Invoke("ReleaseAll");
        }
    }
}

public sealed class GameplayInterruptedException(string message) : Exception(message);

