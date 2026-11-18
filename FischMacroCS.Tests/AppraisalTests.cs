using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class AppraisalTests
{
    private sealed class Clock : IClock
    {
        public long Timestamp { get; private set; }
        public double ElapsedMilliseconds(long since) => Timestamp - since;
        public void Delay(int ms, CancellationToken token) { token.ThrowIfCancellationRequested(); Timestamp += ms; }
    }

    [Fact]
    public async Task ReadsRecordedPlainRollAndItsRepeatChoice()
    {
        using var screen = new Mat(600, 800, MatType.CV_8UC3, Scalar.All(0));
        void Paste(string file, int x, int y)
        {
            using var crop = Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", file));
            using var region = new Mat(screen, new Rect(x, y, crop.Width, crop.Height));
            crop.CopyTo(region);
        }
        Paste("appraisal-held.png", 298, 323);
        Paste("appraisal-choice.png", 373, 269);
        await Task.Run(() =>
        {
            using var vision = new AppraisalVision(800, 600, "Driftwood", "Poisoned", r => new Mat(screen, r), default);
            Assert.Equal(AppraisalReading.Other, vision.Held().Reading);
            Assert.Equal(3L, vision.Choice()?.Cost);
            Paste("appraisal-cash.png", 679, 553);
            Assert.NotNull(vision.Balance());
            screen.SetTo(Scalar.All(0));
            Paste("appraisal-hexed-held.png", 309, 319);

            Assert.NotEqual(new AppraisalResult(AppraisalReading.Other, "Driftwood"), vision.Held());

        });
    }

    [Fact]
    public async Task RecognizesMaximizedAppraiser()
    {
        using var screen = new Mat(1369, 3440, MatType.CV_8UC3, Scalar.All(0));
        foreach (var (file, x, y) in new[] { ("held", 1500, 757), ("choice", 1650, 613), ("cash", 3050, 1306) })
        {
            using var crop = Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", $"appraisal-{file}-large.png"));
            using var region = new Mat(screen, new Rect(x, y, crop.Width, crop.Height));
            crop.CopyTo(region);
        }
        await Task.Run(() =>
        {
            using var vision = new AppraisalVision(3440, 1369, "Driftwood", "Poisoned", r => new Mat(screen, r), default);
            Assert.Equal(new AppraisalResult(AppraisalReading.Other, "Driftwood"), vision.Held());
            Assert.Equal(3L, vision.Choice()?.Cost);
            var before = Assert.IsType<AppraisalBalance>(vision.Balance());
            Assert.Equal(0, before.Difference(vision.Balance()!));
            using var cashBefore = Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "appraisal-cash-before-large.png"));
            using (var panel = new Mat(screen, new Rect(3050, 1306, cashBefore.Width, cashBefore.Height))) cashBefore.CopyTo(panel);
            Assert.True(before.Difference(vision.Balance()!) > .012);
        });
    }

    [Fact]
    public async Task StopsOnWinningFrameEvenUnderTheNpcDialogue()
    {
        using var screen = new Mat(1369, 3440, MatType.CV_8UC3, Scalar.All(0));
        using var crop = Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "appraisal-poisoned-held.png"));
        using (var target = new Mat(screen, new Rect(1550, 753, crop.Width, crop.Height))) crop.CopyTo(target);
        await Task.Run(() =>
        {
            using var vision = new AppraisalVision(3440, 1369, "Driftwood", "Poisoned", r => new Mat(screen, r), default);
            var clock = new Clock();
            string outcome = AppraisalWorkflow.Run(vision.Held, () => throw new Exception("Should already be done"),
                () => throw new Exception("Should not seek another paid choice"), _ => Assert.Fail("Clicked the winning item"),
                ms => clock.Delay(ms, default), clock, default, 0, _ => {});
            Assert.Contains("Found Poisoned Driftwood", outcome);
        });
    }

    [Theory]
    [InlineData("", AppraisalReading.Other)]
    [InlineData("Big Frozen", AppraisalReading.Other)]
    [InlineData("Shiny Poisoned", AppraisalReading.Target)]
    [InlineData("Pois0ned", AppraisalReading.Unreadable)]
    [InlineData("Unpoisoned", AppraisalReading.Unreadable)]
    internal void RecognizesWholeMutation(string modifiers, AppraisalReading expected) =>
        Assert.Equal(expected, AppraisalWorkflow.Identify(modifiers, "Driftwood", "poisoned").Reading);

    [Theory]
    [InlineData("target", 3)]
    [InlineData("already-target", 0)]
    [InlineData("unreadable", 1)]
    [InlineData("limit", 2)]
    [InlineData("cancel", 1)]
    [InlineData("stale", 1)]

    public void RequiresFreshResultBeforeSpendingAgain(string scenario, int expectedClicks)
    {
        var clock = new Clock();
        using var stop = new CancellationTokenSource();
        var plain = new AppraisalResult(AppraisalReading.Other, "Driftwood");
        var target = new AppraisalResult(AppraisalReading.Target, "Poisoned Driftwood");
        int clicks = 0;
        AppraisalBalance? Balance()
        {
            if (scenario == "unreadable" && clicks > 0) return null;
            var ink = new byte[8];
            ink[scenario == "stale" ? 0 : clicks] = 255;
            return new(8, ink, 1);
        }
        string Run() => AppraisalWorkflow.Run(() => scenario == "already-target" || clicks == 3 ? target : plain, Balance,
            () => new AppraisalQuote(new Point(3, 5), 3), _ =>
            {
                clicks++;
                if (scenario == "cancel") stop.Cancel();
                Assert.True(clicks <= expectedClicks);
            }, ms => clock.Delay(ms, stop.Token), clock, stop.Token, scenario == "limit" ? 2 : 0, _ => {});
        if (scenario == "cancel") Assert.Throws<OperationCanceledException>(() => Run());
        else
        {
            string outcome = Run();
            Assert.Contains(scenario switch
            {
                "target" or "already-target" => "Found Poisoned Driftwood",
                "unreadable" => "cash counter",
                "limit" => "Roll limit reached",

                _ => "cash counter"
            }, outcome);
        }
        Assert.Equal(expectedClicks, clicks);
    }
}
