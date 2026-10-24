using FischMacroCS.Core;

namespace FischMacroCS.Tests;

public class ReelActuationPlanTests
{
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
