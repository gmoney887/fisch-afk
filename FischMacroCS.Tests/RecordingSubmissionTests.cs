using System;
using System.IO;
using System.IO.Compression;
using FischMacroCS.Core;
using Xunit;

namespace FischMacroCS.Tests;

public class RecordingSubmissionTests
{
    [Fact]
    public void SanitizeText_RemovesWindowsUserDirectories()
    {
        string raw = @"Error at C:\Users\garre\AppData\Local\Roblox\logs\log.txt while processing C:\Users\garre\Videos\session.avi";
        string sanitized = RecordingSubmissionService.SanitizeText(raw);

        Assert.DoesNotContain(@"C:\Users\garre", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("garre", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[USER_HOME]", sanitized);
    }

    [Fact]
    public void SanitizeText_RemovesMachineHostAndEnvironmentUser()
    {
        string currentUser = Environment.UserName;
        string currentMachine = Environment.MachineName;

        string raw = $"Running on {currentMachine} under user {currentUser} in C:\\Users\\{currentUser}\\FischAFK";
        string sanitized = RecordingSubmissionService.SanitizeText(raw);

        Assert.DoesNotContain(currentUser, sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(currentMachine, sanitized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateDiagnosticBundle_PackagesFilesAndSanitizesContent()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FischAFK_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Set up a mock session directory
            string currentUser = Environment.UserName;
            File.WriteAllText(Path.Combine(tempDir, "summary.txt"), $"Outcome: Escaped\nPath: C:\\Users\\{currentUser}\\clips\\session.avi\nDuration: 4.2s");
            File.WriteAllText(Path.Combine(tempDir, "telemetry.csv"), $"Frame,Time,BarX,TargetX,UserPath\n1,0.016,0.50,0.52,C:\\Users\\{currentUser}\\log");
            File.WriteAllText(Path.Combine(tempDir, "gameplay.avi"), "MOCK_VIDEO_BINARY_DATA");

            var pkgResult = RecordingSubmissionService.CreateDiagnosticBundle(tempDir);
            Assert.True(pkgResult.Success, pkgResult.ErrorMessage);
            Assert.True(File.Exists(pkgResult.ZipPath), "Diagnostic zip should be created");

            using (ZipArchive archive = ZipFile.OpenRead(pkgResult.ZipPath))
            {
                Assert.NotNull(archive.GetEntry("gameplay.avi"));
                Assert.NotNull(archive.GetEntry("summary.txt"));
                Assert.NotNull(archive.GetEntry("telemetry.csv"));
                Assert.NotNull(archive.GetEntry("system_diagnostic.json"));

                // Verify summary.txt inside zip is sanitized
                ZipArchiveEntry summaryEntry = archive.GetEntry("summary.txt")!;
                using var reader = new StreamReader(summaryEntry.Open());
                string unzippedSummary = reader.ReadToEnd();

                Assert.DoesNotContain(currentUser, unzippedSummary, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("[USER_HOME]", unzippedSummary);
            }

            if (File.Exists(pkgResult.ZipPath)) File.Delete(pkgResult.ZipPath);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
