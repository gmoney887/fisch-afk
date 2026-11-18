using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using System.Text.Json;

namespace FischMacroCS.Tests;

public class AncientHammerheadLaserTests(Xunit.Abstractions.ITestOutputHelper output)
{
    internal static Mat Frame(int id) => Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "ancient-hammerhead-nov06", $"frame_{id:D8}.png"));

    internal static Mat Track(Mat frame) => frame.Width == 3440
        ? new Mat(frame, new Rect(968, frame.Height == 1369 ? 1013 : 0, 1504, frame.Height == 1369 ? 273 : 259)) : frame.Clone();

    [Fact]
    public void FixturesAreUnchangedCopiesOfTheSavedFight()
    {
        string directory = System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "ancient-hammerhead-nov06");
        using var provenance = JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(directory, "provenance.json")));
        foreach (var f in provenance.RootElement.GetProperty("Frames").EnumerateArray())
            Assert.Equal(f.GetProperty("SHA256").GetString(), Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(
                    System.IO.Path.Combine(directory, f.GetProperty("File").GetString()!)))));
    }

    [Theory]
    [InlineData(612)]
    [InlineData(613)]
    [InlineData(614)]
    public void RecordedFlashingWarningHasTheAnnotatedBounds(int id)
    {
        using var frame = Frame(id);
        using var crop = Track(frame);
        var zone = ReelLaserDetector.Find(crop, 968, 1013, 1369);
        Assert.NotNull(zone);
        Assert.InRange(zone.Value.Left, 1641, 1649);
        Assert.InRange(zone.Value.Right, 2058, 2066);
    }

    [Theory]
    [InlineData(588)]
    [InlineData(600)]
    [InlineData(605)]
    [InlineData(609)]
    [InlineData(610)]
    [InlineData(615)]
    [InlineData(616)]
    [InlineData(620)]
    [InlineData(623)]
    [InlineData(1310)]
    [InlineData(1588)]
    [InlineData(1610)]
    [InlineData(1616)]
    public void RadarRodSlashesTintedBarsAndCompanionCaptionsAreNotLasers(int id)
    {
        using var frame = Frame(id);
        using var crop = Track(frame);
        Assert.Null(ReelLaserDetector.Find(crop, 968, frame.Height == 342 ? 1027 : 1013, 1369));
    }

    [Theory]
    [InlineData("oath_red_475.png")]
    [InlineData("oath_slash_297.png")]
    [InlineData("oath_brown_484.png")]
    public void OtherRecordedRuinousReelsDoNotTriggerAvoidance(string name)
    {
        using var crop = Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures",name));
        Assert.Null(ReelLaserDetector.Find(crop, 968, 1013, 1369));
    }

    [Fact]
    public void FullViewportAndScaledReelReadTheSameWarning()
    {
        using var frame = Frame(612);
        Assert.Equal(ReelLaserDetector.Find(frame, 0, 0, 1369), new ReelLaserZone(1645, 2062));
        using var scaled = new Mat();
        Cv2.Resize(frame, scaled, new Size(1920, 764));
        var zone = ReelLaserDetector.Find(scaled, 0, 0, 764);
        Assert.NotNull(zone);
        Assert.InRange(zone.Value.Left, 915, 922);
        Assert.InRange(zone.Value.Right, 1147, 1155);
    }

    [Fact]
    public void WarningScanFitsTheActiveReelLatencyBudget()
    {
        using var frame = Frame(613);
        var times = new List<double>();
        for (int i=0; i<40; i++)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            var zone = ReelLaserDetector.Find(frame,968,1013,1369);
            double elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Assert.NotNull(zone);
            if(i>=5)times.Add(elapsed);
        }
        times.Sort();
        double p95=times[(int)Math.Ceiling(times.Count*.95)-1];
        output.WriteLine($"Original613 pulse scan: p95={p95:F3}ms, samples={times.Count}");
        Assert.True(p95<=5,$"Pulse scan p95={p95:F3}ms exceeds its5ms share of the existing25ms observation budget.");
    }

    [Theory]
    [InlineData(1400, true)]
    [InlineData(1700, true)]
    [InlineData(1900, false)]
    [InlineData(2200, false)]
    public void WholeBarEscapesOnItsNearestClearSide(double bar, bool left)
    {
        var avoidance = new ReelLaserAvoidance();
        Assert.True(avoidance.TryTarget(0, new(1645,2062), bar, 210, 1720, 1025, 2415, out double target));
        if (left) Assert.True(target + 105 < 1645);
        else Assert.True(target - 105 > 2062);
    }

    [Theory]
    [InlineData(1040, 1400, false)]
    [InlineData(2000, 2390, true)]
    public void WarningNearRailChoosesTheSideThatCanFitTheBar(int left, int right, bool escapeLeft)
    {
        var avoidance = new ReelLaserAvoidance();
        Assert.True(avoidance.TryTarget(0, new(left,right), (left+right)/2, 210, 1720, 1025, 2415, out double target));
        Assert.InRange(target, 1130, 2310);
        Assert.True(escapeLeft ? target + 105 < left : target - 105 > right);
    }

    [Fact]
    public void SceneryBeyondTheRecordedRailIsNotAnEscapeLane()
    {
        var avoidance = new ReelLaserAvoidance();
        // Saved3440x1369 layout: capture968..2472; visible rail1025..2415.
        // Left would be nearer, but its entire210px body cannot fit there.
        Assert.True(avoidance.TryTarget(0,new(1230,1647),1400,210,1720,968,2472,out double target));
        Assert.True(target-105>1647);
        Assert.InRange(target,1130,2310);
    }

    [Fact]
    public void FlashGapIsBoundedAndDoesNotSwitchEscapeSides()
    {
        var avoidance = new ReelLaserAvoidance();
        Assert.True(avoidance.TryTarget(100, new(1645,2062), 1700, 210, 1720, 1025, 2415, out double first));
        Assert.True(avoidance.TryTarget(150, new(1645,2062), 1900, 210, 1720, 1025, 2415, out double next));
        Assert.Equal(first, next);
        Assert.True(avoidance.TryTarget(349, null, 1519, 210, 1720, 1025, 2415, out _));
        Assert.False(avoidance.TryTarget(351, null, 1519, 210, 1720, 1025, 2415, out double normal));
        Assert.Equal(1720, normal);
        Assert.True(avoidance.TryTarget(400, new(1645,2062), 1900, 210, 1720, 1025, 2415, out double right));
        Assert.True(right - 105 > 2062);
        avoidance.Reset();
        Assert.False(avoidance.TryTarget(401, null, 1900, 210, 1720, 1025, 2415, out _));
    }
}

public partial class FishingEngineReplayTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordedPulseOverridesCenteringAndThenReturnsToTheFish(bool barInsideWarning)
    {
        using var clear = AncientHammerheadLaserTests.Frame(605);
        using var warning = AncientHammerheadLaserTests.Frame(612);
        if (barInsideWarning)
        {
            // Supplemental control pose: retain the original warning pixels,
            // remove the old body and place the recorded clear-frame body in
            // its path. The false case above replays the untouched full PNG.
            using var vision = new VisionProcessor().ProcessReelContext(clear, MinigameTheme.AutoCalibrate);
            using var oldBody = new Mat(warning, new Rect(1138,1131,214,72));
            oldBody.SetTo(new Scalar(12,12,12));
            using var body = new Mat(clear, new Rect(vision.BarLeft,1131,vision.BarWidth,72));
            using var destination = new Mat(warning, new Rect(1690-vision.BarWidth/2,1131,vision.BarWidth,72));
            body.CopyTo(destination);
        }
        var clock = new Clock { CaptureMs = 45 }; var input = new Input(clock);
        long? reelAt = null;
        using var frames = new Frames(() => reelAt.HasValue && clock.Timestamp-reelAt.Value is >= 300 and < 800
            ? warning.Clone() : clear.Clone(), clock);
        using var engine = new FishingEngine(new Settings { EnableRecording=false, EnableAutoClaimAquarium=false,
            EnableAntiAfk=false, EnableWatchdogRecovery=false, EnableHumanizedJitter=false }, frames, frames,
            clock:clock, desktop:new Desktop { ClientWidth=3440, ClientHeight=1369 }, hardware:input);
        var targets = new List<double>(); var escapeInputs=new List<bool>(); bool resumed=false;
        engine.OnTelemetry += t =>
        {
            if(t.State==MacroState.Reeling)
            {
                reelAt ??= clock.Timestamp;
                // Assert the actual control target during the warning,
                // independently of its status label.
                bool pulseFrame = reelAt.HasValue && clock.Timestamp-reelAt.Value is >= 400 and < 800;
                if(pulseFrame && t.ControlFishX.HasValue)
                    targets.Add(t.ControlFishX.Value + t.BarWidth/2.0);
                if(barInsideWarning && pulseFrame && t.ObservedBarCenter is > 1600 and < 1800)
                    escapeInputs.Add(input.Held);
                if(clock.Timestamp-reelAt.Value>1200 && t.ControlFishX.HasValue && t.ObservedFishX.HasValue)
                    resumed |= Math.Abs(t.ControlFishX.Value-t.ObservedFishX.Value)<10 && !t.Action.Contains("laser", StringComparison.OrdinalIgnoreCase);
            }
            t.Dispose();
        };
        clock.Tick=()=>{if(clock.Timestamp>6000 || reelAt.HasValue && clock.Timestamp-reelAt.Value>1800)engine.Stop();};
        engine.Start();
        try { await engine.Completion.WaitAsync(TimeSpan.FromSeconds(40)); }
        finally { engine.Stop(); }
        Assert.NotEmpty(targets);
        Assert.All(targets, edge=>Assert.True(edge<1645, $"Control bar would enter pulse: right edge {edge}"));
        Assert.True(resumed);
        if(barInsideWarning)
        {
            Assert.NotEmpty(escapeInputs);
            Assert.All(escapeInputs, held=>Assert.False(held, "Do not tap right back into the pulse while escaping left."));
        }
        Assert.Equal(0,engine.UnknownCatches); Assert.Equal(0,engine.TotalFails);
        Assert.False(input.Held);
    }
}
