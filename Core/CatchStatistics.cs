using System.Globalization;

namespace FischMacroCS.Core;

public static class CatchStatistics
{
    public static double SuccessRate(int caught, int lost, int unknown) =>
        caught + lost + unknown == 0 ? 0 : caught * 100.0 / ((long)caught + lost + unknown);

    public static string Display(int caught, int lost, int unknown)
    {
        if (caught + lost + unknown == 0) return "—";
        double rate = SuccessRate(caught, lost, unknown);
        // Never round an imperfect session up to a perfect 100%.
        if (lost + unknown > 0) rate = Math.Min(99.9, rate);
        return rate.ToString("0.0", CultureInfo.CurrentCulture) + "%";
    }
}
