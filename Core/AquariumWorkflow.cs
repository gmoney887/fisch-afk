using OpenCvSharp;

namespace FischMacroCS.Core;

/// <summary>Reviewed personal-aquarium controls, anchored to the viewport center and height.</summary>
public static class AquariumWorkflow
{
    public static readonly WorkflowTarget Navigation = new("aquarium-navigation", .077, .024, .10, 1353, .94, Smooth: true,
        AlternateTemplate: "aquarium-navigation-1009", AlternateReferenceHeight: 1009, BlueText: true, SearchNearbyScales: true, MatchNativeScale: true);
    public static readonly WorkflowTarget Claim = new("aquarium-claim", -.211, .539, .065, 1353, Smooth: true, SearchNearbyScales: true,
        ObservedSearchRadiusInHeights: .25);
    // The zero C$/XP balance is stable; scrolling reward toasts are not.
    public static readonly WorkflowTarget EmptyBalance = new("aquarium-reward", -.194, .594, .065, 1353, .94, Smooth: true, SearchNearbyScales: true, RewardBalanceText: true,
        ObservedSearchRadiusInHeights: .25);
    public static readonly WorkflowTarget Close = new("aquarium-close", .560, .121, .025, 1353, .92, SearchNearbyScales: true, RedGlyph: true,
        ObservedSearchRadiusInHeights: .20);
    public static readonly string[] TemplateNames = [Navigation.Name, Claim.Name, EmptyBalance.Name, Close.Name];

    public static WorkflowResult Run(IClock clock, TemplateWorkflowVision vision, Func<Mat?> capture,
        Action<int> delay, Action<Point> click, CancellationToken cancellation, Action<string>? evidence = null)
    {
        cancellation.ThrowIfCancellationRequested();
        var missing = vision.MissingTemplates(TemplateNames);
        if (missing.Length > 0)
            return new(ActionOutcome.Unknown, "Automation unavailable: reviewed visual templates are missing (" + string.Join(", ", missing) + ").");
        bool Stable(Func<Mat, bool> predicate)
        {
            for (int i = 0; i < 2; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                using var frame = capture();
                if (frame == null || frame.Empty()) throw new GameplayInterruptedException("Invalid aquarium capture.");
                if (!predicate(frame)) return false;
                if (i == 0) delay(100);
            }
            return true;
        }
        bool Panel(Mat frame) => vision.Find(frame, Claim).Found && vision.Find(frame, Close).Found;
        var workflow = new VerifiedWorkflow(clock, vision, capture, delay, evidence);
        bool alreadyOpen = Stable(Panel);
        var opened = alreadyOpen
            ? new WorkflowResult(ActionOutcome.ConfirmedSuccess, "Existing aquarium panel confirmed")
            : workflow.Run([new("Open aquarium", Navigation, Claim,
                click, TimeoutMs: 5000)], cancellation);
        if (opened.Outcome != ActionOutcome.ConfirmedSuccess)
        {
            var cleanup = ClosePanel(clock, vision, capture, delay, click, cancellation, evidence);
            return opened with { RetryableWithoutRecovery = cleanup.Outcome == ActionOutcome.ConfirmedSuccess };
        }

        bool empty = Stable(frame => vision.Find(frame, Claim).Found && vision.Find(frame, EmptyBalance).Found);
        var claimed = empty
            ? new WorkflowResult(ActionOutcome.ConfirmedSuccess, "No unclaimed aquarium rewards")
            : workflow.Run([new("Claim reward", Claim, EmptyBalance, click, 5000, Attempts: 2)], cancellation);

        // Close even after an unconfirmed claim, but only through a freshly detected close button.
        // Cancellation/focus loss still propagates without issuing further input.
        var closed = ClosePanel(clock, vision, capture, delay, click, cancellation, evidence, requireCloseAction: true);
        if (closed.Outcome != ActionOutcome.ConfirmedSuccess) return closed;
        if (claimed.Outcome != ActionOutcome.ConfirmedSuccess)
            return claimed with { RetryableWithoutRecovery = true };
        return new(ActionOutcome.ConfirmedSuccess, empty
            ? "No unclaimed aquarium rewards; aquarium closure confirmed"
            : "Aquarium balance cleared after claim; aquarium closure confirmed", RewardClaimed: !empty);
    }

    public static WorkflowResult ClosePanel(IClock clock, IWorkflowVision vision, Func<Mat?> capture,
        Action<int> delay, Action<Point> click, CancellationToken cancellation, Action<string>? evidence = null,
        bool requireCloseAction = false)
    {
        bool clicked = false;
        int clickAttempts = 0;
        long lastClick = 0;
        int absent = 0, closeMatches = 0;
        double bestCloseConfidence = 0;
        bool claimSeen = false;
        long start = clock.Timestamp;
        do
        {
            cancellation.ThrowIfCancellationRequested();
            using var frame = capture();
            if (frame == null || frame.Empty()) throw new GameplayInterruptedException("Invalid aquarium cleanup capture.");
            var close = vision.Find(frame, Close);
            bool claim = vision.Find(frame, Claim).Found;
            bestCloseConfidence = Math.Max(bestCloseConfidence, close.Confidence);
            claimSeen |= claim;
            absent = !close.Found && !claim ? absent + 1 : 0;
            // Claim animations can temporarily obscure both controls. Once we
            // opened the panel, absence alone cannot prove that we closed it.
            if (absent >= 2 && (!requireCloseAction || clicked))
                return new(ActionOutcome.ConfirmedSuccess, "Aquarium panel closure confirmed");
            closeMatches = close.Found ? closeMatches + 1 : 0;
            if (closeMatches >= 2 && clickAttempts < 3 && (!clicked || clock.ElapsedMilliseconds(lastClick) >= 1000))
            {
                cancellation.ThrowIfCancellationRequested();
                click(close.Center);
                clicked = true;
                clickAttempts++; lastClick = clock.Timestamp; closeMatches = 0;
                evidence?.Invoke("Closing aquarium before resuming fishing");
            }
            delay(100);
        } while (clock.ElapsedMilliseconds(start) < (requireCloseAction ? 8000 : 3000));
        return new(ActionOutcome.Unknown, $"Aquarium panel closure was not fully confirmed (best close confidence {bestCloseConfidence:F3}; close clicks {clickAttempts}; claim seen {claimSeen}).");
    }

}
