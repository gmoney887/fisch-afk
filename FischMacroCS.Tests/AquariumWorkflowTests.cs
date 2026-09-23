using FischMacroCS.Core;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class AquariumWorkflowTests
{
    [Fact]
    public void SecondPcNavigationUsesReviewedTextAtItsNativeScale()
    {
        using var strip = Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "aquarium_nav_1009.png"));
        Assert.False(strip.Empty());
        using var frame = new Mat(1009, 1920, MatType.CV_8UC3, Scalar.Black);
        using (var target = new Mat(frame, new Rect((frame.Width - strip.Width) / 2, 0, strip.Width, strip.Height))) strip.CopyTo(target);
        using var vision = Vision();
        var found = vision.Find(frame, AquariumWorkflow.Navigation);
        Assert.True(found.Found, $"Navigation confidence {found.Confidence}");
        Assert.InRange(found.Center.X, 1060, 1070);
        Assert.InRange(found.Center.Y, 27, 37);
        Assert.False(vision.Find(frame, AquariumWorkflow.Claim).Found);
    }
    private sealed class Clock : IClock
    {
        public long Timestamp { get; set; }
        public double ElapsedMilliseconds(long since) => Timestamp - since;
        public void Delay(int ms, CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); Timestamp += ms; }
    }
    private static Mat Frame(int number, int height = 1353, int? width = null)
    {
        using var original = Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", $"aquarium_{number}.png"));
        var scaled = new Mat();
        Cv2.Resize(original, scaled, new Size((int)Math.Round(original.Width * height / 1353.0), height));
        if (width == null) return scaled;
        using (scaled)
        {
            var frame = new Mat(height, width.Value, MatType.CV_8UC3, Scalar.Black);
            int sourceX = Math.Max(0, (scaled.Width - frame.Width) / 2);
            int targetX = Math.Max(0, (frame.Width - scaled.Width) / 2);
            int copyWidth = Math.Min(scaled.Width, frame.Width);
            using var source = new Mat(scaled, new Rect(sourceX, 0, copyWidth, height));
            using var target = new Mat(frame, new Rect(targetX, 0, copyWidth, height));
            source.CopyTo(target);
            return frame;
        }
    }
    private static TemplateWorkflowVision Vision() => new(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Workflows"));

    [Theory]
    [InlineData(1353, null)]
    [InlineData(1080, 1920)]
    [InlineData(1369, 3440)]
    public void RecordedControlsAndZeroBalanceAreRecognized(int height, int? width)
    {
        using var vision = Vision();
        Assert.Empty(vision.MissingTemplates(AquariumWorkflow.TemplateNames));
        using var closed = Frame(1, height, width);
        using var ready = Frame(4, height, width);
        using var claimed = Frame(5, height, width);
        using var staleToast = Frame(9, height, width);
        foreach (var target in new[] { AquariumWorkflow.Navigation, AquariumWorkflow.Claim, AquariumWorkflow.Close })
        {
            var match = vision.Find(ready, target);
            Assert.True(match.Found, $"{target.Name}: {match.Confidence}");
        }
        Assert.True(vision.Find(closed, AquariumWorkflow.Navigation).Found);
        Assert.False(vision.Find(closed, AquariumWorkflow.Claim).Found);
        Assert.False(vision.Find(closed, AquariumWorkflow.Close).Found);
        Assert.False(vision.Find(staleToast, AquariumWorkflow.Claim).Found);
        Assert.False(vision.Find(staleToast, AquariumWorkflow.EmptyBalance).Found);
        Assert.False(vision.Find(ready, AquariumWorkflow.EmptyBalance).Found);
        var zero = vision.Find(claimed, AquariumWorkflow.EmptyBalance);
        Assert.True(zero.Found, $"Zero balance: {zero.Confidence}");
    }

    [Fact]
    public void MissingNavigationNeverClicks()
    {
        var clock = new Clock();
        using var vision = Vision();
        using var blank = new Mat(1353, 3424, MatType.CV_8UC3, Scalar.All(20));
        int clicks = 0;
        var result = AquariumWorkflow.Run(clock, vision, () => blank.Clone(), ms => clock.Delay(ms, default), _ => clicks++, default);
        Assert.Equal(0, clicks);
        Assert.Equal(ActionOutcome.Unknown, result.Outcome);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void RecordedSequenceConfirmsOnlyFreshClaimsAndCloses(bool alreadyEmpty, bool failedClaim, bool failedClose)
    {
        var clock = new Clock();
        using var vision = Vision();
        int stage = 0;
        var clicks = new List<string>();
        Mat Capture() => Frame(stage == 0 || stage == 3 ? 1 : stage == 2 || alreadyEmpty ? 5 : 4);
        void Click(Point point)
        {
            if (point.Y < 70) { clicks.Add("open"); stage = 1; }
            else if (point.Y < 250) { clicks.Add("close"); if (!failedClose) stage = 3; }
            else { clicks.Add("claim"); if (!failedClaim) stage = 2; }
        }
        var result = AquariumWorkflow.Run(clock, vision, Capture, ms => clock.Delay(ms, default), Click, default);
        Assert.Equal(alreadyEmpty ? new[] { "open", "close" } : new[] { "open", "claim", "close" }, clicks);
        Assert.Equal(failedClaim || failedClose ? ActionOutcome.Unknown : ActionOutcome.ConfirmedSuccess, result.Outcome);
        Assert.Equal(!alreadyEmpty && !failedClaim && !failedClose, result.RewardClaimed);
    }

    [Fact]
    public void CancellationAfterOpeningSendsNoClaimOrClose()
    {
        var clock = new Clock();
        using var vision = Vision();
        using var cancellation = new CancellationTokenSource();
        int clicks = 0;
        Assert.ThrowsAny<OperationCanceledException>(() => AquariumWorkflow.Run(clock, vision, () => Frame(1),
            ms => clock.Delay(ms, cancellation.Token), _ => { clicks++; cancellation.Cancel(); }, cancellation.Token));
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void ScheduleUsesPersistedCheckAndMonotonicTimeIncludingZeroTimestamp()
    {
        var clock = new Clock();
        var now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var schedule = new AquariumSchedule(clock);
        Assert.False(schedule.IsDue(now.AddMinutes(-14), 15, now));
        clock.Timestamp += 60000;
        Assert.True(schedule.IsDue(now, 15, now.AddHours(-1)));
        schedule.Checked();
        Assert.False(schedule.IsDue(now, 15, now));
        clock.Timestamp += 15 * 60000;
        Assert.True(schedule.IsDue(now, 15, now));
        Assert.True(new AquariumSchedule(clock).IsDue(DateTime.MinValue, 15, now));
        Assert.True(new AquariumSchedule(clock).IsDue(now.AddMinutes(-16), 15, now));
    }
}
