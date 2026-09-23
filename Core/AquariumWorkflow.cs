using OpenCvSharp;

namespace FischMacroCS.Core;

/// <summary>Reviewed personal-aquarium controls, anchored to the viewport center and height.</summary>
public static class AquariumWorkflow
{
    public static readonly WorkflowTarget Navigation = new("aquarium-navigation", .077, .024, .10, 1353, .94, Smooth: true,
        AlternateTemplate: "aquarium-navigation-1009", AlternateReferenceHeight: 1009, BlueText: true, SearchNearbyScales: true);
    public static readonly WorkflowTarget Claim = new("aquarium-claim", -.211, .539, .065, 1353, Smooth: true, SearchNearbyScales: true);
    // The zero C$/XP balance is stable; scrolling reward toasts are not.
    public static readonly WorkflowTarget EmptyBalance = new("aquarium-reward", -.194, .594, .065, 1353, .94, Smooth: true, SearchNearbyScales: true);
    public static readonly WorkflowTarget Close = new("aquarium-close", .560, .121, .025, 1353, .92, Smooth: true, SearchNearbyScales: true);
    public static readonly string[] TemplateNames = [Navigation.Name, Claim.Name, EmptyBalance.Name, Close.Name];

    public static WorkflowResult Run(IClock clock, TemplateWorkflowVision vision, Func<Mat?> capture,
        Action<int> delay, Action<Point> click, CancellationToken cancellation, Action<string>? evidence = null)
    {
        cancellation.ThrowIfCancellationRequested();
        var missing = vision.MissingTemplates(TemplateNames);
        if (missing.Length > 0)
            return new(ActionOutcome.Unknown, "Automation unavailable: reviewed visual templates are missing (" + string.Join(", ", missing) + ").");
        var workflow = new VerifiedWorkflow(clock, vision, capture, delay, evidence);
        bool attemptedOpen = false;
        var opened = workflow.Run([new("Open aquarium", Navigation, Claim,
            point => { attemptedOpen = true; click(point); }, TimeoutMs: 5000)], cancellation);
        if (opened.Outcome != ActionOutcome.ConfirmedSuccess)
        {
            if (attemptedOpen) return opened;
            using var latest = capture();
            // A manually opened aquarium is not a harmless missing-navigation case.
            bool panelAbsent = latest != null && !latest.Empty() && !vision.Find(latest, Claim).Found && !vision.Find(latest, Close).Found;
            return opened with { RetryableWithoutRecovery = panelAbsent };
        }

        bool empty = true;
        for (int i = 0; i < 2; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            using var frame = capture();
            if (frame == null || frame.Empty()) throw new GameplayInterruptedException("Invalid aquarium capture.");
            empty &= vision.Find(frame, Claim).Found && vision.Find(frame, EmptyBalance).Found;
            if (i == 0) delay(100);
        }
        var claimed = empty
            ? new WorkflowResult(ActionOutcome.ConfirmedSuccess, "No unclaimed aquarium rewards")
            : workflow.Run([new("Claim reward", Claim, EmptyBalance, click, 5000)], cancellation);

        // Close even after an unconfirmed claim, but only through a freshly detected close button.
        // Cancellation/focus loss still propagates without issuing further input.
        var closed = workflow.Run([new("Close aquarium", Close, Claim, click, ExpectedPresent: false)], cancellation);
        if (closed.Outcome != ActionOutcome.ConfirmedSuccess) return closed;
        if (claimed.Outcome != ActionOutcome.ConfirmedSuccess) return claimed;
        return new(ActionOutcome.ConfirmedSuccess, empty
            ? "No unclaimed aquarium rewards; aquarium closure confirmed"
            : "Aquarium balance cleared after claim; aquarium closure confirmed", RewardClaimed: !empty);
    }
}
