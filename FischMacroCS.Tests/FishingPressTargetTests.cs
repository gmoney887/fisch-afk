using FischMacroCS.Core;

namespace FischMacroCS.Tests;

public class FishingPressTargetTests
{
    [Theory]
    [InlineData(3440, 1369)]
    [InlineData(1920, 1080)]
    [InlineData(1280, 720)]
    public void CoveredRightSideChoosesUncoveredLeftWithoutMovingDashboard(int width, int height)
    {
        var point = FishingPressTarget.Find(width, height, p => p.X < width / 2);
        Assert.NotNull(point);
        Assert.InRange(point.Value.X, width / 2 - (int)(height * .29), width / 2 - (int)(height * .27));
        Assert.InRange(point.Value.Y, (int)(height * .41), (int)(height * .43));
    }

    [Fact]
    public void CompletelyCoveredViewCannotAuthorizeAHold()
        => Assert.Null(FishingPressTarget.Find(1920, 1080, _ => false));
}
