using FischMacroCS.Core;

namespace FischMacroCS.Tests;

public class ReelPulseModulatorTests
{
    [Theory]
    [InlineData(70, false)] [InlineData(120, false)] [InlineData(200, false)]
    [InlineData(70, true)] [InlineData(120, true)] [InlineData(200, true)]
    public void SaturatedBrakingOrPursuitOverridesAccruedPulseDebt(int elapsed, bool pull)
    {
        var pulses = new ReelPulseModulator();
        // The previous requested command and actual guarded edge disagree
        // across an observation, accruing debt in the opposite direction.
        // This reproduces the post-dodge0%-but-still-held failure without
        // asserting private balance values or counting vision frames.
        pulses.Force(0, !pull);
        pulses.Input(0, pull);
        Assert.Equal(pull, pulses.Choose(elapsed, pull ? 1 : 0));
        pulses.Input(elapsed, pull);
        Assert.Equal(pull, pulses.Choose(elapsed + 20, pull ? 1 : 0));
    }

    [Theory]
    [InlineData(.38)]
    [InlineData(.40)]
    [InlineData(.44)]
    public void VariableObservationDurationsDeliverRequestedHeldTime(double duty)
    {
        var pulses = new ReelPulseModulator();
        double now = 0, held = 0;
        int[] intervals = [40, 120, 70, 210, 25, 180];
        for (int i = 0; i < 600; i++)
        {
            bool down = pulses.Choose(now, duty); pulses.Input(now, down);
            int interval = intervals[i % intervals.Length];
            if (down) held += interval;
            now += interval;
        }
        Assert.InRange(held / now, duty - .015, duty + .015);
    }

    [Fact]
    public void InputEdgesInsideAnObservationIntervalAreAccountedFor()
    {
        var pulses = new ReelPulseModulator();
        pulses.Force(0, true); pulses.Input(0, true);
        pulses.Choose(50, .4); // Desired time is now fractional, hardware still held.
        pulses.Input(80, false); // Only 30 ms of the interval were held.
        Assert.True(pulses.Choose(150, .4));
        pulses.Reset();
        Assert.False(pulses.Choose(0, .4));
    }
}
