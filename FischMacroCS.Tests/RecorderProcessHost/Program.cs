using System.IO;
using System.Text.Json.Serialization;
using FischMacroCS.Core;

string root = args[0];
using var blocked = new Blocked();
using var recorder = new FlightRecorder(root);
recorder.StartSession(100, 80);
recorder.RecordEvent("blocked-writer", blocked);
try
{
    if (!blocked.Entered.Wait(TimeSpan.FromSeconds(15))) throw new Exception(recorder.LastError ?? "Writer did not start");
    if (args[1] == "draining") recorder.StopSession();
    string pendingReady = Path.Combine(root, "owner-ready.pending");
    File.WriteAllText(pendingReady, recorder.CurrentSessionDirectory!);
    File.Move(pendingReady, Path.Combine(root, "owner-ready.txt"));
    if (!SpinWait.SpinUntil(() => File.Exists(Path.Combine(root, "owner-release.txt")), TimeSpan.FromSeconds(30)))
        throw new TimeoutException("Parent did not release writer");
}
finally { blocked.Release.Set(); }

sealed class Blocked : IDisposable
{
    [JsonIgnore] public ManualResetEventSlim Entered { get; } = new();
    [JsonIgnore] public ManualResetEventSlim Release { get; } = new();
    public string Value { get { Entered.Set(); if (!Release.Wait(TimeSpan.FromSeconds(45))) throw new TimeoutException(); return "finished"; } }
    public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
}
