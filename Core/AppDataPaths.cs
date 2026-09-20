using System.IO;
namespace FischMacroCS.Core;

public static class AppDataPaths
{
    public static IEnumerable<string> RecordingRoots(string current)
    {
        return new[] { current, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recordings") }
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
    // Hosts such as the regression runner can isolate storage before loading application types.
    // Ordinary desktop startup leaves this process-local override unset.
    public static string Root { get; } = Path.GetFullPath(
        AppContext.GetData("FischAFKPro.DataRoot") as string ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FischAFKPro"));
    public static string FilePath(string name)
    {
        Directory.CreateDirectory(Root);
        return Path.Combine(Root, name);
    }
}
