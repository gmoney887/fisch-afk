using System.IO;
using System.Runtime.CompilerServices;
using FischMacroCS.Core;

namespace FischMacroCS.Tests;

internal static class TestStorage
{
    internal static string Root { get; private set; } = "";

    [ModuleInitializer]
    internal static void Initialize()
    {
        Root = Path.Combine(Path.GetTempPath(), "fisch-test-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        AppContext.SetData("FischAFKPro.DataRoot", Root);
    }
}

public class StorageIsolationTests
{
    [Fact]
    public void SavingValidSettingsPreservesPreviousConfigurationAsBackup()
    {
        string path = Path.Combine(TestStorage.Root, "config.json");
        new Settings { RodSlot = "4", EnableAntiAfk = false, ShakeMode = "Disabled" }.Save();
        new Settings { RodSlot = "6", EnableAntiAfk = true, ShakeMode = "Visual" }.Save();
        var current = Settings.Load();
        Assert.Equal("6", current.RodSlot); Assert.True(current.EnableAntiAfk);
        Assert.Equal("Visual", current.ShakeMode);
        var previous = System.Text.Json.JsonSerializer.Deserialize<Settings>(File.ReadAllText(path + ".backup"))!;
        Assert.Equal("4", previous.RodSlot); Assert.False(previous.EnableAntiAfk);
        Assert.Equal("Disabled", previous.ShakeMode);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Theory]
    [InlineData("{broken", true)]
    [InlineData("null", true)]
    [InlineData("{broken", false)]
    public void CorruptSettingsRecoverWithoutDestroyingEvidenceOrGoodBackup(string damaged, bool validBackup)
    {
        string path = Path.Combine(TestStorage.Root, "config.json");
        string backup = validBackup ? "{\"RodSlot\":\"4\",\"ToggleHotkey\":\"F2\",\"CastHoldMs\":0,\"ReelTimeoutMs\":60000}" : "broken backup";
        File.WriteAllText(path, damaged);
        File.WriteAllText(path + ".backup", backup);
        var recovered = Settings.Load();
        Assert.Equal(validBackup ? "4" : "1", recovered.RodSlot);
        Assert.Equal("F6", recovered.ToggleHotkey);
        Assert.True(recovered.CastHoldMs > 0);
        Assert.True(recovered.ReelTimeoutMs <= 40000);
        Assert.Equal(damaged, File.ReadAllText(path));
        Assert.Contains(Directory.GetFiles(TestStorage.Root, "config.json.unreadable-*"),
            file => File.ReadAllText(file) == damaged);
        recovered.Save();
        Assert.Equal(backup, File.ReadAllText(path + ".backup"));
        Assert.Equal(recovered.RodSlot, Settings.Load().RodSlot);
    }

    [Fact]
    public void DefaultSettingsLogsAndRecordingsUseTheIsolatedTestHost()
    {
        Assert.Equal(TestStorage.Root, AppDataPaths.Root);
        Assert.Equal(Path.Combine(TestStorage.Root, "macro_events.log"), SessionLogger.Instance.LogFilePath);
        using var recorder = new FlightRecorder();
        Assert.Equal(Path.Combine(TestStorage.Root, "recordings"), recorder.RecordingsDirectory);
        new Settings().Save();
        Assert.True(File.Exists(Path.Combine(TestStorage.Root, "config.json")));
    }
}
