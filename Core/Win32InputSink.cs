using FischMacroCS.Native;

namespace FischMacroCS.Core;

/// <summary>Hardware adapter. GameplayInput owns validation, cancellation and held-state tracking.</summary>
internal sealed class Win32InputSink : IInputSink
{
    public void SendHardwareMouseDown(int sx = -1, int sy = -1, int cx = -1, int cy = -1, IntPtr window = default) => Win32.SendHardwareMouseDown(sx, sy, cx, cy, window);
    public void SendHardwareMouseUp(int sx = -1, int sy = -1, int cx = -1, int cy = -1, IntPtr window = default) => Win32.SendHardwareMouseUp(sx, sy, cx, cy, window);
    public void SendHardwareMouseMove(int sx, int sy, int cx = -1, int cy = -1, IntPtr window = default) => Win32.SendHardwareMouseMove(sx, sy, cx, cy, window);
    public void SendHardwareClick(int sx, int sy, int cx = -1, int cy = -1, IntPtr window = default) => throw new NotSupportedException("Clicks must use the guarded input sink.");
    public void SendRelativeMove(int x, int y) => Win32.SendRelativeMove(x, y);
    public void SetCursorPos(int x, int y) => Win32.SetCursorPos(x, y);
    public void SendKeyPress(char key) => throw new NotSupportedException();
    public void SendKeyString(string text, int delayMs = 40) => throw new NotSupportedException();
    public void SelectAllAndClear() => throw new NotSupportedException();
    public void mouse_event(int flags, int x, int y, int data, int extra) => Win32.SendMouseEvent((uint)flags, x, y, (uint)data);
    public void keybd_event(byte key, byte scan, uint flags, int extra) => Win32.SendKeyboardEvent(key, flags);
    public void ReleaseAll() => throw new NotSupportedException("Held inputs belong to GameplayInput.");
}
