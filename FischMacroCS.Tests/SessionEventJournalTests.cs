using System.IO;
using System.Text.Json;
using FischMacroCS.Core;

namespace FischMacroCS.Tests;

public sealed class SessionEventJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fisch-incident-journal-" + Guid.NewGuid().ToString("N"));
    public SessionEventJournalTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    private static void Write(SessionEventJournal journal, string kind, int sequence, string? action = null)
    {
        var data = JsonSerializer.SerializeToElement(new { Action = action, Sequence = sequence });
        journal.WriteLine(kind, data, JsonSerializer.Serialize(new { Kind = kind, Data = data }));
    }

    [Fact]
    public void RoutineChurnCannotEraseEarlyIncidentOrKeyboardEvidence()
    {
        using (var journal = new SessionEventJournal(_root, 256, 512))
        {
            Write(journal, "recovery-context", 0);
            Write(journal, "input", 1, "KeyDown:32");
            Write(journal, "input", 2, "KeyUp:32");
            for (int i = 0; i < 10000; i++)
                Write(journal, (i % 4) switch { 0 => "frame", 1 => "decision", 2 => "cast-observation", _ => "input" }, i, "RelativeMove");
            Write(journal, "stop-request", 10000);
            journal.Flush();
            Assert.True(journal.ExpiredEntries > 9900);
            Assert.Equal(0, journal.IncidentExpiredEntries);
        }
        string[] retained = Directory.GetFiles(_root, "incidents*.jsonl").SelectMany(File.ReadLines).ToArray();
        Assert.Equal(4, retained.Length);
        Assert.Contains(retained, line => line.Contains("KeyDown:32"));
        Assert.Contains(retained, line => line.Contains("KeyUp:32"));
        Assert.Contains(retained, line => line.Contains("recovery-context"));
        Assert.Contains(retained, line => line.Contains("stop-request"));
        Assert.DoesNotContain(File.ReadAllText(Path.Combine(_root, "events.jsonl")), "KeyDown:32");
    }

    [Fact]
    public void RecoveryStormRemainsBoundedAndReportsIncidentExpiry()
    {
        using (var journal = new SessionEventJournal(_root, 256, 256))
        {
            for (int i = 0; i < 10000; i++) Write(journal, "fishing-retry", i);
            journal.Flush();
            Assert.True(journal.IncidentExpiredEntries > 9900);
        }
        var files = Directory.GetFiles(_root, "incidents*.jsonl");
        Assert.Equal(4, files.Length);
        Assert.All(files, file => Assert.InRange(new FileInfo(file).Length, 1, 256));
        Assert.Contains("9999", File.ReadAllText(Path.Combine(_root, "incidents.jsonl")));
        foreach (var line in files.SelectMany(File.ReadLines))
        {
            using var parsed = JsonDocument.Parse(line);
            Assert.Equal("fishing-retry", parsed.RootElement.GetProperty("Kind").GetString());
        }
    }

    [Theory]
    [InlineData("MouseDown:400,500")]
    [InlineData("MouseUp")]
    [InlineData("ReleaseAll")]
    [InlineData(null)]
    public void RoutineInputsDoNotConsumeIncidentBudget(string? action)
    {
        using (var journal = new SessionEventJournal(_root))
            Write(journal, "input", 1, action);
        Assert.Empty(File.ReadAllText(Path.Combine(_root, "incidents.jsonl")));
        Assert.NotEmpty(File.ReadAllText(Path.Combine(_root, "events.jsonl")));
    }
}
