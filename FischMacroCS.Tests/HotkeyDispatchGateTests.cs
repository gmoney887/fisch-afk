using FischMacroCS.Core;

namespace FischMacroCS.Tests;

public class HotkeyDispatchGateTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnePressDeliveredByBothRoutesOnlyTogglesOnce(bool hookFirst)
    {
        var gate = new HotkeyDispatchGate();
        Assert.True(gate.TryDispatch(0x75, 100, hookFirst));
        Assert.False(gate.TryDispatch(0x75, 105, !hookFirst));
        Assert.True(gate.TryDispatch(0x75, 300, hookFirst));
        Assert.False(gate.TryDispatch(0x75, 305, !hookFirst));
    }

    [Fact]
    public void FallbackStillWorksWhenAnInstalledHookStopsDelivering()
    {
        var gate = new HotkeyDispatchGate();
        Assert.True(gate.TryDispatch(0x75, 100, true));
        Assert.False(gate.TryDispatch(0x75, 100, false));
        Assert.True(gate.TryDispatch(0x75, 500, false));
        Assert.True(gate.TryDispatch(0x75, 700, false));
    }

    [Fact]
    public void RapidSeparatePressesAndOtherKeysArePreserved()
    {
        var gate = new HotkeyDispatchGate();
        Assert.True(gate.TryDispatch(0x75, 100, true));
        Assert.True(gate.TryDispatch(0x75, 125, true));
        Assert.True(gate.TryDispatch(0x23, 125, false));
        Assert.False(gate.TryDispatch(0x75, 125, false));
    }

    [Fact]
    public void DuplicateDetectionHandlesWindowsTimestampWraparound()
    {
        var gate = new HotkeyDispatchGate();
        Assert.True(gate.TryDispatch(0x75, uint.MaxValue - 5, true));
        Assert.False(gate.TryDispatch(0x75, 4, false));
        Assert.True(gate.TryDispatch(0x75, 200, false));
    }
}
