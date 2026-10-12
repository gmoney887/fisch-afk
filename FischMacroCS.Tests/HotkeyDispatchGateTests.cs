using FischMacroCS.Core;

namespace FischMacroCS.Tests;

public class HotkeyDispatchGateTests
{
    [Fact]
    public void AutomatedEndCannotTriggerFallbackEvenWithoutHookDelivery()
    {
        var gate = new HotkeyDispatchGate();
        gate.RecordInjected(0x23, 100);
        Assert.False(gate.TryDispatch(0x23, 100, false));
        Assert.True(gate.TryDispatch(0x23, 101, false));
    }

    [Fact]
    public void DelayedInjectedNotificationsStayBlockedAfterOtherInjectedKeys()
    {
        var gate = new HotkeyDispatchGate();
        gate.RecordInjected(0x23, 100);
        gate.RecordInjected(0x23, 200);
        gate.RecordInjected(0x75, 300);
        Assert.False(gate.TryDispatch(0x23, 100, false));
        Assert.False(gate.TryDispatch(0x23, 200, false));
        Assert.False(gate.TryDispatch(0x75, 300, false));
        Assert.True(gate.TryDispatch(0x75, 301, true));
        Assert.False(gate.TryDispatch(0x75, 301, false));
    }

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
