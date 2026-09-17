using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FischMacroCS.Core;
using Xunit;

namespace FischMacroCS.Tests;

public class SessionLoggerTests : IDisposable
{
    private readonly string _testLogFile;
    private readonly SessionLogger _logger;

    public SessionLoggerTests()
    {
        _testLogFile = Path.Combine(Path.GetTempPath(), $"macro_test_{Guid.NewGuid():N}.log");
        _logger = new SessionLogger(_testLogFile);
    }

    [Fact]
    public void SessionLogger_WritesStateAndInputEvents()
    {
        _logger.LogState(MacroState.Stopped, MacroState.Casting, "Test Start");
        _logger.LogInput("MouseDown", 500, 600, 100, 200);
        _logger.LogVision("CastBar", "Fill=85%, Pred=98%");
        _logger.Flush();

        var recent = _logger.GetRecentLogs();
        Assert.Contains(recent, e => e.Contains("[STATE  ]") && e.Contains("Stopped -> Casting"));
        Assert.Contains(recent, e => e.Contains("[INPUT  ]") && e.Contains("MouseDown") && e.Contains("Screen=(500, 600)"));
        Assert.Contains(recent, e => e.Contains("[VISION ]") && e.Contains("Fill=85%"));

        Assert.True(File.Exists(_testLogFile));
        string fileContent;
        using (var fs = new FileStream(_testLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(fs, System.Text.Encoding.UTF8))
        {
            fileContent = reader.ReadToEnd();
        }
        Assert.Contains("Stopped -> Casting", fileContent);
        Assert.Contains("MouseDown", fileContent);
    }

    [Fact]
    public void SessionLogger_HandlesConcurrentWritesSafely()
    {
        // Concurrently write from 20 parallel tasks
        Parallel.For(0, 50, i =>
        {
            _logger.Log("TEST", $"Concurrent message {i}");
        });

        _logger.Flush();
        var recent = _logger.GetRecentLogs();
        Assert.True(recent.Length >= 50);
    }

    public void Dispose()
    {
        _logger.Dispose();
        try
        {
            if (File.Exists(_testLogFile)) File.Delete(_testLogFile);
            if (File.Exists(_testLogFile + ".old")) File.Delete(_testLogFile + ".old");
        }
        catch { }
    }
}
