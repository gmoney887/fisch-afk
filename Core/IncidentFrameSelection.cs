using System.IO;
using System.Text.Json;

namespace FischMacroCS.Core;

public static class IncidentFrameSelection
{
    public static string[] Read(string directory)
    {
        string incidentPath = Path.Combine(directory, "first-incident.json");
        string indexPath = Path.Combine(directory, "frame-index.json");
        if (!File.Exists(incidentPath) || !File.Exists(indexPath)) return [];
        using var incident = JsonDocument.Parse(File.ReadAllText(incidentPath));
        using var index = JsonDocument.Parse(File.ReadAllText(indexPath));
        long at = incident.RootElement.GetProperty("Timestamp").GetInt64();
        long frequency = incident.RootElement.GetProperty("TimestampFrequency").GetInt64();
        return index.RootElement.EnumerateArray().Where(row =>
        {
            long time = row.GetProperty("Timestamp").GetInt64();
            return time >= at - frequency * 15 && time <= at + frequency * 5 &&
                row.GetProperty("Data").TryGetProperty("Context", out var context) && context.GetBoolean();
        }).OrderBy(row => row.GetProperty("Timestamp").GetInt64())
            .Select(row => row.GetProperty("File").GetString()!)
            .Where(name => Path.GetFileName(name) == name && File.Exists(Path.Combine(directory, name)))
            .Take(24).ToArray();
    }
}
