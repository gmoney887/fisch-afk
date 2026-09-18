using FischMacroCS.Core;
using FischMacroCS.Native;

namespace FischMacroCS.Tests;

public class GameplayInputTests
{
    private sealed class Hardware : IInputSink
    {
        public List<string> Events { get; } = new();
        public void SendHardwareMouseDown(int sx = -1, int sy = -1, int cx = -1, int cy = -1, IntPtr window = default) => Events.Add("down");
        public void SendHardwareMouseUp(int sx = -1, int sy = -1, int cx = -1, int cy = -1, IntPtr window = default) => Events.Add($"up:{window}:{cx}:{cy}");
        public void SendHardwareMouseMove(int sx, int sy, int cx = -1, int cy = -1, IntPtr window = default) => Events.Add("move");
        public void SendHardwareClick(int sx, int sy, int cx = -1, int cy = -1, IntPtr window = default) => throw new NotSupportedException();
        public void SendRelativeMove(int x, int y) => Events.Add("relative");
        public void SetCursorPos(int x, int y) => Events.Add("cursor");
        public void SendKeyPress(char key) => throw new NotSupportedException();
        public void SendKeyString(string text, int delayMs = 40) => throw new NotSupportedException();
        public void SelectAllAndClear() => throw new NotSupportedException();
        public void mouse_event(int flags, int x, int y, int data, int extra) => Events.Add($"mouse:{flags}");
        public void keybd_event(byte key, byte scan, uint flags, int extra) => Events.Add($"key:{key}:{flags}");
        public void ReleaseAll() => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("Focus lost")]
    [InlineData("Viewport changed")]
    [InlineData("Invalid capture")]
    public void InterruptionReleasesAllHeldInputsBeforePropagating(string reason)
    {
        var hardware = new Hardware(); bool valid = true;
        var input = new GameplayInput(() => { if (!valid) throw new GameplayInterruptedException(reason); }, _ => { }, hardware: hardware);
        input.SendHardwareMouseDown(cx: 12, cy: 34, window: (IntPtr)99);
        input.keybd_event(65, 0, 0, 0);
        input.mouse_event((int)Win32.MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
        valid = false;
        Assert.Throws<GameplayInterruptedException>(() => input.SendRelativeMove(1, 0));
        Assert.Contains("up:99:12:34", hardware.Events);
        Assert.Contains($"key:65:{Win32.KEYEVENTF_KEYUP}", hardware.Events);
        Assert.Contains($"mouse:{Win32.MOUSEEVENTF_RIGHTUP}", hardware.Events);
        Assert.DoesNotContain("relative", hardware.Events);
        int count = hardware.Events.Count;
        input.ReleaseAll(); Assert.Equal(count, hardware.Events.Count);
    }

    [Fact]
    public void CancellationDuringClickDelayAlwaysReleasesMouse()
    {
        var hardware = new Hardware();
        var input = new GameplayInput(() => { }, _ => throw new OperationCanceledException(), hardware: hardware);
        Assert.Throws<OperationCanceledException>(() => input.SendHardwareClick(100, 100, 10, 10, (IntPtr)1));
        Assert.Equal(new[] { "move", "down", "up:1:10:10" }, hardware.Events);
    }
}
