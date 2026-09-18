using System.IO;
namespace FischMacroCS.Core;

public static class AppDataPaths
{
    public static IEnumerable<string> RecordingRoots(string current)
    {
        return new[] { current, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recordings") }
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FischAFKPro");
    public static string FilePath(string name)
    {
        Directory.CreateDirectory(Root);
        return Path.Combine(Root, name);
    }
}
