using FischMacroCS.Core;
using FischMacroCS.Native;

namespace FischMacroCS.Tests;

public class GameplayInputTests
{
    private sealed class Hardware : IInputSink
    {
        public List<string> Events { get; } = new();
        public bool RejectNextKeyRelease { get; set; }
        public string? RejectedMouseOperation { get; set; }
        public int RejectedRightReleases { get; set; }
        public Action<byte, uint>? KeyEdge { get; set; }
        private void Mouse(string operation)
        {
            Events.Add(operation);
            Win32.RequireInputAccepted(1, RejectedMouseOperation == operation ? 0u : 1u, operation);
        }
        public void SendHardwareMouseDown(int sx = -1, int sy = -1, int cx = -1, int cy = -1, IntPtr window = default) => Mouse("down");
        public void SendHardwareMouseUp(int sx = -1, int sy = -1, int cx = -1, int cy = -1, IntPtr window = default) => Events.Add($"up:{window}:{cx}:{cy}");
        public void SendHardwareMouseMove(int sx, int sy, int cx = -1, int cy = -1, IntPtr window = default) => Mouse("move");
        public void SendHardwareClick(int sx, int sy, int cx = -1, int cy = -1, IntPtr window = default) => throw new NotSupportedException();
        public void SendRelativeMove(int x, int y) => Events.Add("relative");
        public void SetCursorPos(int x, int y) => Events.Add("cursor");
        public void SendKeyPress(char key) => throw new NotSupportedException();
        public void SendKeyString(string text, int delayMs = 40) => throw new NotSupportedException();
        public void SelectAllAndClear() => throw new NotSupportedException();
        public void mouse_event(int flags, int x, int y, int data, int extra)
        {
            Events.Add($"mouse:{flags}");
            if ((flags & (int)Win32.MOUSEEVENTF_RIGHTUP) != 0 && RejectedRightReleases > 0)
            { RejectedRightReleases--; Win32.RequireInputAccepted(1, 0, "right up"); }
        }
        public void keybd_event(byte key, byte scan, uint flags, int extra)
        {
            Events.Add($"key:{key}:{flags}");
            KeyEdge?.Invoke(key, flags);
            if (flags == Win32.KEYEVENTF_KEYUP && RejectNextKeyRelease)
            { RejectNextKeyRelease = false; throw new GameplayInterruptedException("Input rejected"); }
        }
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
    public void RejectedMoveAbortsClickBeforePressingMouse()
    {
        var hardware = new Hardware { RejectedMouseOperation = "move" };
        var input = new GameplayInput(() => { }, _ => Assert.Fail("Should not hold"), hardware: hardware);
        Assert.Throws<GameplayInterruptedException>(() => input.SendHardwareClick(100, 100));
        Assert.Equal(new[] { "move" }, hardware.Events);
    }

    [Fact]
    public void RejectedPressStillAttemptsReleaseWithoutHolding()
    {
        var hardware = new Hardware { RejectedMouseOperation = "down" };
        var input = new GameplayInput(() => { }, _ => Assert.Fail("Should not hold"), hardware: hardware);
        Assert.Throws<GameplayInterruptedException>(() => input.SendHardwareClick(100, 100, 12, 34, (IntPtr)99));
        Assert.Equal(new[] { "move", "down", "up:99:12:34" }, hardware.Events);
    }

    [Fact]
    public void RejectedRightUpRemainsOwnedAfterCleanupAlsoFails()
    {
        var hardware = new Hardware { RejectedRightReleases = 2 };
        var input = new GameplayInput(() => { }, _ => { }, hardware: hardware);
        input.mouse_event((int)Win32.MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
        Assert.Throws<GameplayInterruptedException>(() => input.mouse_event((int)Win32.MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0));
        Assert.Equal(3, hardware.Events.Count); // down, rejected up, rejected cleanup up
        input.ReleaseAll(); Assert.Equal(4, hardware.Events.Count);
        input.ReleaseAll(); Assert.Equal(4, hardware.Events.Count);
    }

    [Theory]
    [InlineData(1u, 0u)]
    [InlineData(2u, 1u)]
    public void IncompleteNativeDeliveryIsAnInterruption(uint expected, uint accepted)
    {
        var error = Assert.Throws<GameplayInterruptedException>(() => Win32.RequireInputAccepted(expected, accepted, "mouse"));
        Assert.Contains($"{accepted}/{expected}", error.Message);
    }

    [Fact]
    public void RejectedReleaseDoesNotSkipOtherInputsAndRemainsOwnedForRetry()
    {
        var hardware = new Hardware();
        var input = new GameplayInput(() => { }, _ => { }, hardware: hardware);
        input.keybd_event(65, 0, 0, 0); input.keybd_event(66, 0, 0, 0);
        hardware.RejectNextKeyRelease = true;
        input.ReleaseAll();
        Assert.Contains($"key:65:{Win32.KEYEVENTF_KEYUP}", hardware.Events);
        Assert.Contains($"key:66:{Win32.KEYEVENTF_KEYUP}", hardware.Events);
        int afterFirst = hardware.Events.Count;
        input.ReleaseAll(); Assert.Equal(afterFirst + 1, hardware.Events.Count);
        input.ReleaseAll(); Assert.Equal(afterFirst + 1, hardware.Events.Count);
    }

    [Fact]
    public void CancellationDuringClickDelayAlwaysReleasesMouse()
    {
        var hardware = new Hardware();
        var input = new GameplayInput(() => { }, _ => throw new OperationCanceledException(), hardware: hardware);
        Assert.Throws<OperationCanceledException>(() => input.SendHardwareClick(100, 100, 10, 10, (IntPtr)1));
        Assert.Equal(new[] { "move", "down", "up:1:10:10" }, hardware.Events);
    }
    [Theory]
    [InlineData("", false)]
    [InlineData("carp", false)]
    [InlineData("crates", true)]
    [InlineData("old saved search", true)]
    [InlineData("cratecrate", true)]
    public void ReplacingSavedSingleLineTextWorksEvenWhenControlAIsIgnored(string saved, bool ignoreControlA)
    {
        string text = saved;
        int caret = saved.Length / 2, anchor = caret;
        bool control = false, shift = false;
        var hardware = new Hardware();
        hardware.KeyEdge = (key, flags) =>
        {
            bool down = flags == 0;
            if (key == 0x11) { control = down; return; }
            if (key == 0x10) { shift = down; return; }
            if (!down) return;
            if (key == 0x41 && control)
            {
                if (!ignoreControlA) { anchor = 0; caret = text.Length; }
                return;
            }
            if (key is 0x24 or 0x23)
            {
                caret = key == 0x24 ? 0 : text.Length;
                if (!shift) anchor = caret;
                return;
            }
            int left = Math.Min(caret, anchor), length = Math.Abs(caret - anchor);
            if (length > 0) { text = text.Remove(left, length); caret = anchor = left; }
            if (key == 8)
            {
                if (length == 0 && caret > 0) { text = text.Remove(caret - 1, 1); caret--; }
            }
            else { text = text.Insert(caret, char.ToLowerInvariant((char)key).ToString()); caret++; }
            anchor = caret;
        };
        var input = new GameplayInput(() => { }, _ => { }, hardware: hardware);
        for (int pass = 0; pass < 2; pass++)
        {
            input.SelectAllAndClear();
            Assert.Equal("", text);
            input.SendKeyString("crate");
            Assert.Equal("crate", text);
        }
        Assert.False(control);
        Assert.False(shift);
    }

    [Fact]
    public void CancellationDuringFallbackSelectionReleasesShift()
    {
        bool shiftHeld = false;
        var hardware = new Hardware { KeyEdge = (key, flags) => { if (key == 0x10) shiftHeld = flags == 0; } };
        var input = new GameplayInput(() => { }, _ => { if (shiftHeld) throw new OperationCanceledException(); }, hardware: hardware);
        Assert.Throws<OperationCanceledException>(() => input.SelectAllAndClear());
        Assert.False(shiftHeld);
    }

}
