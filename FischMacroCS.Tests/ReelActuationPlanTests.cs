using FischMacroCS.Core;

namespace FischMacroCS.Tests;

public class ReelActuationPlanTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DragonEdgeCommandSurvivesInterFramePulsesAndYieldsToFreshEvidence(bool right)
    {
        // Frame 587 of the Nov 16 dragon and its mirror. A centering pulse
        // briefly released the hold between two consecutive emergency decisions.
        double direction = right ? 1 : -1;
        var plan = new ReelActuationPlan();
        void Configure(double at) => plan.Configure(at, 0, direction * 463, direction * 143,
            direction * 284, 696, 519, right, barHalfWidth: 173);
        Configure(100);
        plan.KeepEdgeInput(right);
        foreach (int at in new[] { 116, 132 })
        {
            Assert.True(plan.TryDuty(at, out double duty));
            Assert.Equal(right ? 1d : 0d, duty);
            plan.Input(at, right);
        }
        Assert.True(plan.TryDuty(148, out double resumed));
        Assert.NotEqual(right ? 1d : 0d, resumed);

        Configure(150);
        plan.KeepEdgeInput(right);
        Configure(158);
        Assert.True(plan.TryDuty(158, out double fresh));
        Assert.NotEqual(right ? 1d : 0d, fresh);
    }

    [Fact]
    public void ActuationCannotRenewItsOriginalEvidenceLease()
    {
        var plan = new ReelActuationPlan();
        plan.Configure(100, 500, 20, 510, 0, 600, 400, false);
        for (int time = 100; time <= 340; time += 16)
        {
            Assert.True(plan.TryDuty(time, out double duty));
            Assert.InRange(duty, 0, 1);
            plan.Input(time, duty > .5);
        }
        Assert.True(plan.TryDuty(350, out _));
        Assert.False(plan.TryDuty(351, out _));
        plan.Input(352, true);
        Assert.False(plan.TryDuty(353, out _));
    }

    [Theory]
    [InlineData("reset")]
    [InlineData("clock")]
    public void StopOrClockDiscontinuityRequiresFreshConfiguration(string reason)
    {
        var plan = new ReelActuationPlan();
        plan.Configure(100, 500, 20, 510, 0, 600, 400, true);
        Assert.True(plan.TryDuty(150, out _));
        if (reason == "reset") plan.Reset(); else plan.Input(90, false);
        Assert.False(plan.TryDuty(160, out _));
        plan.Configure(200, 500, 0, 500, 0, 600, 400, false);
        Assert.True(plan.TryDuty(200, out _));
    }
}
