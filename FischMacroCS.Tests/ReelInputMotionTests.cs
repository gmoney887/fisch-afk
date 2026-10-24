using FischMacroCS.Core;
namespace FischMacroCS.Tests;
public class ReelInputMotionTests
{
    [Fact]
    public void CaptureVelocityAccountsForBothGuardedEdgesInsideTheInterval()
    {
        var motion=new ReelInputMotion();motion.Input(0,true);motion.Input(100,false);
        // Independent two-stage plant: initialv=-12, +650for100ms, -410for100ms.
        // Positions:0 ->2.05 ->5.30; terminalv=12, averagev=26.5.
        Assert.Equal(12,motion.CaptureVelocity(0,200,0,5.30,650,410),8);
        var aged=motion.Project(200,250,5.30,12,650,410);
        Assert.Equal(5.3875,aged.Position,8);Assert.Equal(-8.5,aged.Velocity,8);
    }
    [Fact]
    public void AGapOrClockReversalDoesNotExtrapolatePositions()
    {
        var motion=new ReelInputMotion();motion.Input(0,true);
        Assert.Equal(0,motion.CaptureVelocity(0,300,10,15,650,410));
        Assert.Equal((15d,20d),motion.Project(300,600,15,20,650,410));
        Assert.Equal((15d,20d),motion.Project(300,200,15,20,650,410));
    }
}
