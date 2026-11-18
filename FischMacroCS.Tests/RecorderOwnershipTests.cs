using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FischMacroCS.Core;

namespace FischMacroCS.Tests;

public sealed class RecorderOwnershipTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fisch-owner-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("active")]
    [InlineData("draining")]
    [InlineData("crash")]
    public async Task RetentionRespectsOtherProcessAndReclaimsEvidenceAfterItExits(string mode)
    {
        Directory.CreateDirectory(_root);
        string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "dotnet.exe");
        if (!File.Exists(dotnet)) dotnet = "dotnet";
        var start = new ProcessStartInfo(dotnet) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "RecorderProcessHost", "RecorderProcessHost.dll"));
        start.ArgumentList.Add(_root); start.ArgumentList.Add(mode);
        using var child = Process.Start(start)!;
        var stderr = child.StandardError.ReadToEndAsync();
        var stdout = child.StandardOutput.ReadToEndAsync();
        try
        {
            string ready = Path.Combine(_root, "owner-ready.txt");
            Assert.True(SpinWait.SpinUntil(() => File.Exists(ready) || child.HasExited, TimeSpan.FromSeconds(20)));
            Assert.False(child.HasExited, child.HasExited ? await stderr : "Child unexpectedly exited");
            string active = File.ReadAllText(ready);
            Directory.SetCreationTimeUtc(active, DateTime.UtcNow.AddDays(-8));
            string completed = Path.Combine(_root, "session_completed");
            Directory.CreateDirectory(completed);
            File.WriteAllText(Path.Combine(completed, "summary.txt"), "completed");
            using var other = new FlightRecorder(_root, maxRecordings: 1);
            other.EnforceRetentionPolicy();
            Assert.True(File.Exists(Path.Combine(active, "manifest.json")), "Other process lost its manifest");
            Assert.True(File.Exists(Path.Combine(active, "events.jsonl")));
            Assert.Null(other.LastError);
            Assert.False(Directory.Exists(completed));

            if (mode == "crash") child.Kill(entireProcessTree: true);
            else File.WriteAllText(Path.Combine(_root, "owner-release.txt"), "release");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (mode != "crash") Assert.True(child.ExitCode == 0, await stderr);
            other.EnforceRetentionPolicy();
            Assert.False(Directory.Exists(active)); // released/abandoned ownership must not pin evidence forever
            Assert.Null(other.LastError);
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
            await stdout; await stderr;
        }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
