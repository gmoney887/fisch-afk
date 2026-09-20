using System.IO;

namespace FischMacroCS.Tests;

/// <summary>Private recordings are optional in CI, but absence must never be a passing test.</summary>
public sealed class LocalFixtureFactAttribute : FactAttribute
{
    public LocalFixtureFactAttribute(params string[] paths)
    {
        var missing = paths.Where(path => !File.Exists(Resolve(path)) && !Directory.Exists(Resolve(path))).ToArray();
        if (missing.Length > 0) Skip = "Private fixture unavailable: " + string.Join(", ", missing);
    }

    public static string Resolve(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            foreach (var root in new[] { directory.FullName, Path.Combine(directory.FullName, "FischMacroCS.Tests") })
            {
                string path = Path.Combine(root, relative);
                if (File.Exists(path) || Directory.Exists(path)) return path;
            }
        }
        return Path.Combine(AppContext.BaseDirectory, relative);
    }
}
