using OpenCvSharp;

namespace FischMacroCS.Core;

internal enum AppraisalReading { Missing, Unreadable, Other, Target }
internal readonly record struct AppraisalResult(AppraisalReading Reading, string Name = "");

internal readonly record struct AppraisalQuote(Point Button, long Cost);

internal sealed record AppraisalBalance(int Width, byte[] Pixels, int InkCount)
{
    internal double Difference(AppraisalBalance other)
    {
        if (Pixels.Length != other.Pixels.Length || Width != other.Width) return 1;
        using var first = Mat.FromPixelData(Pixels.Length / Width, Width, MatType.CV_8UC1, Pixels);
        using var second = Mat.FromPixelData(Pixels.Length / Width, Width, MatType.CV_8UC1, other.Pixels);
        return Cv2.Norm(first, second, NormTypes.L1) / (255 * (double)Math.Max(InkCount, other.InkCount));
    }
}

internal static class AppraisalWorkflow
{
    // Only positively identified modifiers are evidence that another paid roll is appropriate.
    internal static readonly string[] Modifiers = ("Big Giant Shiny Sparkling Mourned Shrouded Crimson Fallen Mythical Abyssal Spirit " +
        "Greedy Boreal Vined Fossilized Scorched Hexed Midas Lunar Electric Glossy Silver Coral Mosaic Frozen Darkened Translucent " +
        "Negative Amber Albino Poisoned Decayed").Split(' ');

    internal static AppraisalResult Identify(string modifiers, string item, string mutation)
    {
        string[] words = modifiers.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string name = string.Join(" ", words.Append(item));
        string padded = " " + string.Join(" ", words) + " ";
        if (padded.Contains(" " + mutation + " ", StringComparison.OrdinalIgnoreCase))
            return new(AppraisalReading.Target, name);
        return words.All(w => Modifiers.Contains(w, StringComparer.OrdinalIgnoreCase))
            ? new(AppraisalReading.Other, name) : new(AppraisalReading.Unreadable);
    }

    internal static string Run(Func<AppraisalResult> held, Func<AppraisalBalance?> balance, Func<AppraisalQuote?> choice,
        Action<Point> click, Action<int> delay, IClock clock, CancellationToken cancellation, int limit, Action<string> progress)
    {
        int confirmed = 0;
        long spent = 0;
        string Status(string message) => $"{confirmed} paid roll{(confirmed == 1 ? "" : "s")}, {spent:N0} C$ spent. {message}";
        bool Wait(Func<bool> check)
        {
            long start = clock.Timestamp;
            do
            {
                cancellation.ThrowIfCancellationRequested();
                if (check()) return true;
                delay(100);
            } while (clock.ElapsedMilliseconds(start) < 12000);
            return false;
        }

        AppraisalResult current = default;
        bool ReadEquipped()
        {
            AppraisalResult previous = default;
            return Wait(() =>
            {
                current = held();
                if (current.Reading == AppraisalReading.Target) return true;
                bool stable = current.Reading == AppraisalReading.Other && current == previous;
                previous = current;
                return stable;
            });
        }

        while (true)
        {
            if (!ReadEquipped())
                return Status("Could not read the equipped item and its mutation. Keep the name and weight visible.");
            if (current.Reading == AppraisalReading.Target) return Status($"Found {current.Name}.");
            if (confirmed > 0)
            {
                progress(Status($"Last result: {current.Name}."));
                if (limit > 0 && confirmed >= limit) return Status($"Roll limit reached. Last result: {current.Name}.");
            }
            AppraisalBalance? before = null;
            if (!Wait(() => (before = balance()) != null)) return Status("Could not read the C$ balance.");
            AppraisalQuote? quote = null;
            if (!Wait(() => (quote = choice()).HasValue))
                return Status("Open the Appraiser's “Can you appraise it again?” choice with the fee visible.");
            if (!ReadEquipped()) return Status("Equipped item changed or became unreadable.");
            if (current.Reading == AppraisalReading.Target) return Status($"Found {current.Name}.");
            var latest = choice();
            if (latest == null || latest.Value.Cost != quote!.Value.Cost) return Status("Appraisal choice or fee changed.");
            long fee = latest.Value.Cost;

            cancellation.ThrowIfCancellationRequested();
            click(latest.Value.Button);
            progress(Status($"Waiting for the {fee:N0} C$ charge for roll {confirmed + 1}."));

            // An appraisal receipt can name the old mutation. Require a visibly different
            // cash counter that settles on two captures before examining the new item.
            AppraisalBalance? previous = null;
            if (!Wait(() =>
                {
                    var after = balance();
                    bool charged = after != null && before!.Difference(after) > .012 &&
                        previous != null && previous.Difference(after) < .006;
                    previous = after;
                    return charged;
                }))
                return Status("The cash counter did not confirm the last click. Check your balance and equipped item.");
            spent += fee;
            confirmed++;
            progress(Status("Checking the equipped item."));
            if (!Wait(() => choice().HasValue)) return Status("The repeat appraisal choice did not return.");
        }
    }
}
