using System.IO;

namespace FischMacroCS.Core;

/// <summary>One session/report budget plus the independently bounded raw incident archive.</summary>
internal static class RecordingStorageBudget
{
    public const long SessionAndReportLimit = 1024L * 1024 * 1024;

    public static long SessionAndReportBytes(string root)
    {
        if (!Directory.Exists(root)) return 0;
        // Reports can also be prepared from a selected directory outside the
        // recordings root. Do not charge unrelated sibling folders (e.g. Temp).
        var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint };
        return Directory.EnumerateDirectories(root, "session_*", options).Sum(FlightRecorder.StoredBytes)
            + FlightRecorder.StoredBytes(Path.Combine(root, "exports"))
            + FlightRecorder.StoredBytes(Path.Combine(root, "incident-archive", "exports"));
    }

    public static long IncidentBytes(string archive) => Count(archive, "exports", _ => 0);

    private static long Count(string root, string separateDirectory, Func<string, long> countSeparate)
    {
        if (!Directory.Exists(root)) return 0;
        var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint };
        long total = Directory.EnumerateFiles(root, "*", options).Sum(file => new FileInfo(file).Length);
        foreach (string dir in Directory.EnumerateDirectories(root, "*", options))
            total += string.Equals(Path.GetFileName(dir), separateDirectory, StringComparison.OrdinalIgnoreCase)
                ? countSeparate(dir) : FlightRecorder.StoredBytes(dir);
        return total;
    }
}
