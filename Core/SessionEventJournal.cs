using System.Text.Json;

namespace FischMacroCS.Core;

/// <summary>Keep low-volume outcomes/recovery and keyboard evidence independent of per-frame churn.</summary>
public sealed class SessionEventJournal : IDisposable
{
    private readonly BoundedEventJournal _routine, _incidents;
    public long ExpiredEntries => _routine.ExpiredEntries;
    public long IncidentExpiredEntries => _incidents.ExpiredEntries;

    public SessionEventJournal(string directory, long routineSegmentBytes = 4L * 1024 * 1024,
        long incidentSegmentBytes = 512L * 1024)
    {
        _routine = new(directory, routineSegmentBytes);
        try { _incidents = new(directory, incidentSegmentBytes, "incidents"); }
        catch { _routine.Dispose(); throw; }
    }

    public void WriteLine(string kind, JsonElement data, string line)
    {
        _routine.WriteLine(line);
        bool retain = kind is not "frame" and not "decision" and not "cast-observation";
        if (kind == "input")
        {
            // Mouse edges/jiggles can occur every control tick. Keep them in the routine
            // journal; retain keyboard edges separately to investigate walking/jump inputs.
            retain = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("Action", out var action) &&
                action.ValueKind == JsonValueKind.String &&
                (action.GetString()!.StartsWith("KeyDown:", StringComparison.Ordinal) ||
                 action.GetString()!.StartsWith("KeyUp:", StringComparison.Ordinal));
        }
        if (retain) _incidents.WriteLine(line);
    }

    public void Flush() { _routine.Flush(); _incidents.Flush(); }
    public void Dispose() { try { _routine.Dispose(); } finally { _incidents.Dispose(); } }
}
