using FischMacroCS.Core;

namespace FischMacroCS.Tests;

public class TelemetryDispatchGateTests
{
    [Fact]
    public void TextUpdatesCannotStarveCameraFramesAndFramesRemainThrottled()
    {
        var clock = new TestClock();
        var gate = new TelemetryDispatchGate(100, clock);
        Assert.True(gate.TryEnter(MacroState.Reeling, out bool text));
        gate.Complete(text);
        clock.Timestamp = 10;
        Assert.True(gate.TryEnter(MacroState.Reeling, out bool frame, hasPreviewFrame: true));
        // A separate frame cadence must still respect the single pending UI callback.
        clock.Timestamp = 110;
        Assert.False(gate.TryEnter(MacroState.Reeling, out _, hasPreviewFrame: true));
        gate.Complete(frame);
        Assert.True(gate.TryEnter(MacroState.Reeling, out text));
        gate.Complete(text);
        clock.Timestamp = 120;
        Assert.True(gate.TryEnter(MacroState.Reeling, out frame, hasPreviewFrame: true));
        gate.Complete(frame);
        clock.Timestamp = 150;
        Assert.False(gate.TryEnter(MacroState.Reeling, out _, hasPreviewFrame: true));
    }

    [Fact]
    public void CadenceLimitsRepeatedStateButDeliversChangesAndStopImmediately()
    {
        var clock = new TestClock();
        var gate = new TelemetryDispatchGate(100, clock);
        Assert.True(gate.TryEnter(MacroState.Reeling, out bool first));
        gate.Complete(first);
        clock.Timestamp = 99;
        Assert.False(gate.TryEnter(MacroState.Reeling, out _));
        clock.Timestamp = 100;
        Assert.True(gate.TryEnter(MacroState.Reeling, out bool next));
        gate.Complete(next);
        Assert.True(gate.TryEnter(MacroState.PostCatch, out bool changed));
        Assert.True(gate.TryEnter(MacroState.Stopped, out bool stopped));
        gate.Complete(stopped);
        gate.Complete(changed);
    }

    private sealed class TestClock : IClock
    {
        public long Timestamp { get; set; }
        public double ElapsedMilliseconds(long since) => Timestamp - since;
        public void Delay(int milliseconds, CancellationToken cancellation) => Timestamp += milliseconds;
    }

    [Fact]
    public void StopIsDeliveredWhilePreviewPendingWithoutUnlockingItsSlot()
    {
        var gate = new TelemetryDispatchGate();
        Assert.True(gate.TryEnter(MacroState.Reeling, out bool preview));
        Assert.True(preview);
        Assert.False(gate.TryEnter(MacroState.Reeling, out _));
        Assert.True(gate.TryEnter(MacroState.Stopped, out bool stopped));
        Assert.False(stopped);
        gate.Complete(stopped);
        Assert.False(gate.TryEnter(MacroState.Casting, out _));
        gate.Complete(preview);
        Assert.True(gate.TryEnter(MacroState.Casting, out bool resumed));
        Assert.True(resumed);
        gate.Complete(resumed);
    }
}
