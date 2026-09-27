using FischMacroCS.Core;
using FischMacroCS.Native;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class CrateWorkflowTests
{
    [Theory]
    [InlineData("crate_ui_15.png", true)]
    [InlineData("crate_quantity-two.png", true)]
    [InlineData("crate_quantity-blank.png", false)]
    public void BatchQuantityAcceptsMultipleAndRejectsBlank(string file, bool expected)
    {
        using var frame = Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", file));
        using var vision = Vision();
        Assert.Equal(expected, vision.QuantityPresent(frame));
    }

    [Fact]
    public void CaptureAndRecognitionTimeCountTowardTheDeadline()
    {
        var clock = new Clock();
        int captures = 0;
        var result = CrateWorkflow.Run(new FakeVision(), () =>
        {
            captures++;
            clock.Delay(2200, default); // Simulate slow capture/recognition work.
            return new Mat(100, 100, MatType.CV_8UC3, Scalar.Black);
        }, ms => clock.Delay(ms, default), _ => { }, _ => { }, _ => { }, default, clock: clock);
        Assert.Equal(ActionOutcome.Unknown, result.Outcome);
        Assert.Contains("Inventory did not open", result.Evidence);
        Assert.Equal(3, captures); // Initial capture plus two bounded polling attempts.
    }

    [Fact]
    public void AlreadyEquippedLiveCrateIsRecognizedBeforeClicking()
    {
        using var frame = Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "crate_selected_current.png"));
        using var vision = Vision();
        Assert.True(vision.Selected(frame, new Point(1645, 1083)));
        Assert.False(vision.Selected(frame, new Point(1577, 1083)));
    }
    [Theory]
    [InlineData("crate_reward_mutated.png")]
    [InlineData("crate_reward_silver.png")]
    public void MutatedBaitCrateRewardIsRecognized(string fixture)
    {
        using var frame = Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        using var vision = Vision();
        Assert.NotNull(vision.Find(frame, "opened"));
        Assert.NotNull(vision.Find(frame, "reward-word"));
    }
    [Fact]
    public void CurrentLiveRewardIsRecognized()
    {
        using var frame = Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "crate_reward_current.png"));
        using var vision = Vision();
        Assert.NotNull(vision.Find(frame, "opened"));
        Assert.NotNull(vision.Find(frame, "reward-word"));
    }
    private static CrateVision Vision() => new(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Workflows"));
    private static Mat Frame(int n) => Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", $"crate_ui_{n}.png"));

    [Theory]
    [InlineData("quantity-two")]
    [InlineData("quantity-blank")]
    [InlineData("wrong-filter")]
    [InlineData("fish-search")]
    public void OtherRecordedStatesCannotAuthorizeOpeningOrEmptySuccess(string name)
    {
        using var vision = Vision();
        using var frame = Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", $"crate_{name}.png"));
        Assert.Null(vision.Find(frame, "one"));
        Assert.Null(vision.Find(frame, "opened"));
        Assert.False(vision.Empty(frame));
        if (name is "wrong-filter" or "fish-search") Assert.Null(vision.Find(frame, "search"));
        if (name == "fish-search") Assert.Null(vision.Find(frame, "item"));
    }

    [Fact]
    public void BlankResultGridRequiresTheVerifiedFilterAndVisibleBag()
    {
        using var vision = Vision(); using var frame = Frame(14);
        Cv2.Rectangle(frame, new Rect(1400, 1000, 640, 270), Scalar.Black, -1);
        Assert.True(vision.Empty(frame));
        Cv2.Rectangle(frame, new Rect(1720, 950, 300, 45), Scalar.Black, -1);
        Assert.False(vision.Empty(frame));
    }

    [Theory]
    [InlineData(1369)]
    [InlineData(1080)]
    [InlineData(1009)]
    public void RecordedStatesAreRecognizedAtSupportedHeights(int height)
    {
        using var vision = Vision();
        Assert.Empty(vision.MissingTemplates());
        foreach (var (n, names) in new[] { (14, new[] { "bag", "search", "item" }),
            (15, new[] { "dialog", "one", "yes" }), (16, new[] { "opened", "reward-word" }) })
        {
            using var source = Frame(n); using var frame = new Mat();
            Cv2.Resize(source, frame, new Size((int)Math.Round(source.Width * height / 1369.0), height));
            foreach (string name in names) Assert.True(vision.Find(frame, name).HasValue, $"{n}, {name}, {height}");
            Assert.False(vision.Empty(frame));
            if (n == 14)
            {
                Assert.True(vision.Selected(frame, new Point((int)(1580 * height / 1369.0), (int)(1045 * height / 1369.0))));
                Assert.False(vision.Selected(frame, new Point((int)(1515 * height / 1369.0), (int)(1045 * height / 1369.0))));
                Assert.Null(vision.Find(frame, "dialog"));
                Assert.Null(vision.Find(frame, "opened"));
            }
            if (n == 15) Assert.Null(vision.Find(frame, "opened"));
            if (n == 16) Assert.Null(vision.Find(frame, "dialog"));
        }
    }

    [Theory]
    [InlineData("crate_inventory_current.png")]
    [InlineData("crate_inventory_native.png")]
    public void CurrentInventoryLayoutIsRecognized(string file)
    {
        using var frame = Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", file));
        using var vision = Vision();
        Assert.NotNull(vision.Find(frame, "bag"));
        Assert.NotNull(vision.Find(frame, "search"));
        Assert.NotNull(vision.Find(frame, "item"));
        Assert.False(vision.Empty(frame));
    }
    private sealed class FakeVision : ICrateVision
    {
        public int Stage;
        public bool Missing, EmptyInventory, WrongQuantity, NoReward, HideSlot;
        public string[] MissingTemplates() => Missing ? ["bag"] : [];
        public Point? Find(Mat frame, string name) => (Stage, name) switch
        {
            (0, "bag-slot") when !HideSlot => new Point(10, 95),
            (1 or 2 or 3, "bag") => new Point(10, 10),
            (2 or 3, "search") => new Point(20, 20),
            (2 or 3, "item") when !EmptyInventory => new Point(30, 30),
            (5 or 6, "dialog") => new Point(40, 40),
            (6, "one") when !WrongQuantity => new Point(50, 50),
            (5 or 6, "yes") => new Point(60, 60),
            (7, "opened") when !NoReward => new Point(80, 80),
            (7, "reward-word") when !NoReward => new Point(90, 80),
            _ => null
        };
        public bool Selected(Mat frame, Point item) => Stage == 3;
        public bool Empty(Mat frame) => Stage == 2 && EmptyInventory;
        public bool QuantityPresent(Mat frame) => Stage == 6 && !WrongQuantity;
    }

    [Theory]
    [InlineData("success")]
    [InlineData("empty")]
    [InlineData("quantity")]
    [InlineData("reward")]
    [InlineData("missing")]
    [InlineData("cancel")]
    [InlineData("bag-key-missed")]
    [InlineData("bag-unavailable")]
    public void WorkflowRequiresSelectionQuantityAndNewReward(string scenario)
    {
        var vision = new FakeVision { Missing = scenario == "missing", EmptyInventory = scenario == "empty",
            WrongQuantity = scenario == "quantity", NoReward = scenario == "reward", HideSlot = scenario == "bag-unavailable" };
        using var cancel = new CancellationTokenSource();
        var clock = new Clock();
        var actions = new List<string>();
        WorkflowResult Run() => CrateWorkflow.Run(vision, () => new Mat(100, 100, MatType.CV_8UC3, Scalar.Black),
            ms => { clock.Delay(ms, cancel.Token); if (scenario == "cancel") cancel.Cancel(); },
            p => { actions.Add("click"); if (vision.Stage is 0 or 2 or 4 || (vision.Stage == 6 && p == new Point(60, 60))) vision.Stage++; },
            k => { actions.Add("bag"); if (vision.Stage != 0 || scenario is not ("bag-key-missed" or "bag-unavailable")) vision.Stage = vision.Stage == 0 ? 1 : 4; },
            s => { actions.Add(s); vision.Stage = s == "crate" ? 2 : 6; }, cancel.Token, clock: clock);
        if (scenario == "cancel") { Assert.ThrowsAny<OperationCanceledException>(() => Run()); return; }
        var result = Run();
        Assert.Equal(scenario is "success" or "empty" ? ActionOutcome.ConfirmedSuccess : ActionOutcome.Unknown, result.Outcome);
        Assert.Equal(scenario is "success", result.RewardClaimed);
        if (scenario == "bag-unavailable") Assert.DoesNotContain("click", actions);
        if (scenario == "missing") Assert.Empty(actions);
        if (scenario == "quantity") Assert.Equal(6, vision.Stage);
        if (scenario == "empty") Assert.DoesNotContain(CrateWorkflow.StackQuantityRequest, actions);
        if (scenario == "success") { Assert.Contains(CrateWorkflow.StackQuantityRequest, actions); Assert.DoesNotContain("1", actions); }
    }

    private sealed class Clock : IClock
    {
        public long Timestamp { get; private set; }
        public double ElapsedMilliseconds(long start) => Timestamp - start;
        public void Delay(int ms, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Timestamp += ms; }
    }
    private sealed class Desktop(Clock clock, bool fail) : GameDesktop
    {
        public int Activations;
        public override IntPtr FindRobloxWindow() => (IntPtr)42;
        public override bool IsIconic(IntPtr w) => clock.Timestamp < 400;
        public override IntPtr GetForegroundWindow() => !fail && clock.Timestamp >= 750 ? (IntPtr)42 : (IntPtr)1;
        public override bool GetClientRect(IntPtr w, out Win32.RECT rect) { rect = new() { Right = 1920, Bottom = 1080 }; return true; }
        public override uint GetDpiForWindow(IntPtr w) => 96;
        public override void ForceSetForegroundWindow(IntPtr w) => Activations++;
        public override void ShowWindowAsync(IntPtr w, int command) { }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartupWaitsForRestoreButNeverBypassesFocus(bool fail)
    {
        var clock = new Clock(); var desktop = new Desktop(clock, fail);
        if (fail) Assert.Throws<GameplayInterruptedException>(() => GameplayStartup.Acquire(desktop, clock, default, () => { }));
        else Assert.Equal((IntPtr)42, GameplayStartup.Acquire(desktop, clock, default, () => { }).Window);
        Assert.InRange(clock.Timestamp, 750, 1500);
        Assert.True(desktop.Activations >= 3);
    }
}
