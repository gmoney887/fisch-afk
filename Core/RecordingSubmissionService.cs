using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Security.Cryptography;
using OpenCvSharp;

namespace FischMacroCS.Core;

public class SubmissionPackageResult
{
    public bool IsReduced { get; set; }
    public bool Success { get; set; }
    public string ZipPath { get; set; } = "";
    public string SummaryText { get; set; } = "";
    public string SessionName { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
}

public sealed record ReportMediaOptions(string[] Frames, int MaskX = 0, int MaskY = 0, int MaskWidth = 0, int MaskHeight = 0);

public class RecordingSubmissionService
{
    private static readonly HttpClient _httpClient = new();

    /// <summary>
    /// Sanitizes text by stripping user home directories, Windows account names, and machine paths.
    /// Gameplay images require separate review before sharing.
    /// </summary>
    public static string SanitizeText(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";

        string sanitized = input;

        // 1. Sanitize standard user paths: C:\Users\<Username>\...
        sanitized = Regex.Replace(sanitized, @"[A-Za-z]:\\[Uu]sers\\[^\\]+", "[USER_HOME]", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"/[Uu]sers/[^/]+", "[USER_HOME]", RegexOptions.IgnoreCase);

        // 2. Sanitize environment user name
        try
        {
            string userName = Environment.UserName;
            if (!string.IsNullOrWhiteSpace(userName) && userName.Length >= 3)
            {
                sanitized = sanitized.Replace(userName, "[USER]", StringComparison.OrdinalIgnoreCase);
            }

            string machineName = Environment.MachineName;
            if (!string.IsNullOrWhiteSpace(machineName) && machineName.Length >= 3)
            {
                sanitized = sanitized.Replace(machineName, "[DEVICE]", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { }

        return sanitized;
    }

    /// <summary>
    /// Packages a recording session directory into an diagnostic zip bundle.
    /// </summary>
    public static SubmissionPackageResult CreateDiagnosticBundle(string sessionDir, FishingEngine? engine = null, ReportMediaOptions? media = null)
    {
        var result = new SubmissionPackageResult();

        if (!Directory.Exists(sessionDir))
        {
            result.Success = false;
            result.ErrorMessage = "Session directory does not exist: " + sessionDir;
            return result;
        }

        try
        {
            string sessionName = Path.GetFileName(sessionDir);
            result.SessionName = sessionName;

            string baseDir = Path.GetDirectoryName(sessionDir) ?? AppDomain.CurrentDomain.BaseDirectory;
            string exportsDir = Path.Combine(baseDir, "exports");
            Directory.CreateDirectory(exportsDir);

            string policy = JsonSerializer.Serialize(media);
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(policy)))[..16];
            string zipPath = Path.Combine(exportsDir, $"{sessionName}_{key}_diagnostic.zip");
            string receipt = zipPath + ".prepared.json";
            if (File.Exists(receipt))
            {
                var frozen = JsonSerializer.Deserialize<SubmissionPackageResult>(File.ReadAllText(receipt));
                if (frozen is { Success: true } && File.Exists(frozen.ZipPath)) return frozen;
            }
            long selectedBytes = Directory.EnumerateFiles(sessionDir).Where(path =>
                media == null || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || media.Frames.Contains(Path.GetFileName(path)))
                .Sum(path => new FileInfo(path).Length);
            long recordingBytes = Directory.EnumerateDirectories(baseDir, "session_*").Sum(FlightRecorder.StoredBytes)
                + FlightRecorder.StoredBytes(exportsDir);
            if (recordingBytes + selectedBytes * 2 + 1024 * 1024 > 1024L * 1024 * 1024)
                throw new IOException("The 1 GB recording/report budget is full. Export or remove old reports, or select fewer frames.");
            // Never replace a previous report whose upload may have an uncertain outcome.
            if (File.Exists(zipPath)) zipPath = Path.Combine(exportsDir, $"{sessionName}_{Guid.NewGuid():N}_diagnostic.zip");

            // Create temporary staging folder
            string tempStageDir = Path.Combine(Path.GetTempPath(), "FischMacro_Submit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempStageDir);

            try
            {
                // 1. Copy gameplay.avi (if exists)
                string aviSrc = Path.Combine(sessionDir, "gameplay.avi");
                if (media == null && File.Exists(aviSrc))
                {
                    File.Copy(aviSrc, Path.Combine(tempStageDir, "gameplay.avi"), true);
                }

                // 2. Sanitize and copy telemetry.csv
                string csvSrc = Path.Combine(sessionDir, "telemetry.csv");
                if (File.Exists(csvSrc))
                {
                    string csvContent = File.ReadAllText(csvSrc);
                    File.WriteAllText(Path.Combine(tempStageDir, "telemetry.csv"), SanitizeText(csvContent), Encoding.UTF8);
                }

                // 3. Sanitize and copy summary.txt
                string summarySrc = Path.Combine(sessionDir, "summary.txt");
                string summaryText = "";
                if (File.Exists(summarySrc))
                {
                    summaryText = SanitizeText(File.ReadAllText(summarySrc));
                    File.WriteAllText(Path.Combine(tempStageDir, "summary.txt"), summaryText, Encoding.UTF8);
                    result.SummaryText = summaryText;

                    // Extract outcome if available
                    var match = Regex.Match(summaryText, @"Session Outcome:\s*([^\r\n]+)");
                    if (match.Success)
                    {
                        result.Outcome = match.Groups[1].Value.Trim();
                    }
                }

                // Session-owned metadata only: never mix current statistics or logs into an older report.
                foreach (string name in new[] { "manifest.json", "events.jsonl", "events.1.jsonl", "events.2.jsonl", "events.3.jsonl",
                    "incidents.jsonl", "incidents.1.jsonl", "incidents.2.jsonl", "incidents.3.jsonl", "completed.json", "frame-index.json" })
                {
                    string source = Path.Combine(sessionDir, name);
                    if (File.Exists(source)) File.WriteAllText(Path.Combine(tempStageDir, name), SanitizeText(File.ReadAllText(source)));
                }
                var diag = new { SchemaVersion = 2, SessionName = sessionName, Outcome = result.Outcome,
                    MetadataAvailable = File.Exists(Path.Combine(sessionDir, "manifest.json")),
                    Privacy = "Text paths are sanitized. Gameplay images may identify players." };
                File.WriteAllText(Path.Combine(tempStageDir, "system_diagnostic.json"), JsonSerializer.Serialize(diag));

                foreach (string frame in Directory.EnumerateFiles(sessionDir, "frame_*.png"))
                {
                    if (media != null && !media.Frames.Contains(Path.GetFileName(frame), StringComparer.Ordinal)) continue;
                    string destination = Path.Combine(tempStageDir, Path.GetFileName(frame));
                    if (media is { MaskWidth: > 0, MaskHeight: > 0 })
                    {
                        using var image = LoadReportImage(frame, media);
                        Cv2.ImWrite(destination, image);
                    }
                    else File.Copy(frame, destination);
                    string label = frame + ".label.json";
                    if (File.Exists(label)) File.WriteAllText(Path.Combine(tempStageDir, Path.GetFileName(label)), SanitizeText(File.ReadAllText(label)));
                }
                File.WriteAllText(Path.Combine(tempStageDir, "media-selection.json"), policy);

                // 6. Compress into zip bundle
                ZipFile.CreateFromDirectory(tempStageDir, zipPath, CompressionLevel.Optimal, false);

                if (new FileInfo(zipPath).Length > 20L * 1024 * 1024)
                {
                    // Original full export remains local; explicitly identify the reduced upload candidate.
                    foreach (string frame in Directory.EnumerateFiles(tempStageDir, "frame_*.png")) File.Delete(frame);
                    string video = Path.Combine(tempStageDir, "gameplay.avi");
                    if (File.Exists(video)) File.Delete(video);
                    zipPath = Path.Combine(exportsDir, $"{sessionName}_{Guid.NewGuid():N}_reduced_diagnostic.zip");
                    ZipFile.CreateFromDirectory(tempStageDir, zipPath, CompressionLevel.Optimal, false);
                    result.SummaryText += "\nReduced bundle: images and video omitted; original export retained locally.";
                    result.IsReduced = true;
                }
                if (new FileInfo(zipPath).Length > 20L * 1024 * 1024)
                    throw new InvalidOperationException("Even the reduced diagnostic exceeds 20 MB. Export locally and select a shorter session.");
                result.Success = true;
                result.ZipPath = zipPath;
                File.WriteAllText(receipt, JsonSerializer.Serialize(result));
            }
            finally
            {
                // Clean up staging directory
                try { Directory.Delete(tempStageDir, true); } catch { }
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    public static Mat LoadReportImage(string path, ReportMediaOptions options)
    {
        var image = Cv2.ImRead(path);
        if (image.Empty()) { image.Dispose(); throw new IOException("Cannot read gameplay image."); }
        var mask = new Rect(Math.Clamp(options.MaskX, 0, image.Width), Math.Clamp(options.MaskY, 0, image.Height),
            Math.Max(0, options.MaskWidth), Math.Max(0, options.MaskHeight));
        mask.Width = Math.Min(mask.Width, image.Width - mask.X);
        mask.Height = Math.Min(mask.Height, image.Height - mask.Y);
        if (mask.Width > 0 && mask.Height > 0) Cv2.Rectangle(image, mask, Scalar.Black, -1);
        return image;
    }

    /// <summary>
    /// Opens the pre-filled GitHub Issues URL and highlights the generated zip in Windows Explorer for drag-and-drop.
    /// </summary>
    public static void OpenGitHubIssueWithBundle(SubmissionPackageResult bundle)
    {
        if (!bundle.Success || !File.Exists(bundle.ZipPath)) return;

        // Reveal zip bundle in Windows Explorer
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{bundle.ZipPath}\"",
                UseShellExecute = true
            });
        }
        catch { }

        // Format GitHub issue URL
        string title = Uri.EscapeDataString($"[Vision/Tracking Report] Session {bundle.SessionName} ({bundle.Outcome})");
        string bodyText = 
            "### Problem Description\n" +
            "Please describe what you observed during this fishing session (e.g. fish escaped, tracking lost needle, wrong hotbar slot):\n\n" +
            "### Diagnostic Summary\n" +
            "```text\n" +
            (string.IsNullOrWhiteSpace(bundle.SummaryText) ? $"Session: {bundle.SessionName}\nOutcome: {bundle.Outcome}" : bundle.SummaryText) + "\n" +
            "```\n\n" +
            "### Diagnostic Bundle Attached\n" +
            "> 📎 **Drag and drop** the highlighted zip file (`" + Path.GetFileName(bundle.ZipPath) + "`) from Windows Explorer into this box to attach the recording & telemetry.";

        string body = Uri.EscapeDataString(bodyText);
        string issueUrl = $"https://github.com/gmoney887/fisch-afk/issues/new?title={title}&body={body}";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = issueUrl,
                UseShellExecute = true
            });
        }
        catch { }
    }

    /// <summary>
    /// Sends the diagnostic bundle and telemetry summary directly to a Discord webhook (if configured).
    /// </summary>
    public static async Task<(bool success, string message)> SubmitToDiscordWebhookAsync(SubmissionPackageResult bundle, string webhookUrl)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            return (false, "Discord Webhook URL is empty.");
        }

        if (!bundle.Success || !File.Exists(bundle.ZipPath))
        {
            return (false, "Diagnostic zip bundle not found.");
        }

        try
        {
            using var form = new MultipartFormDataContent();

            // Prepare embed payload
            var payload = new
            {
                content = $"🎣 **Fisch AFK Pro - Session Diagnostic Report**",
                embeds = new[]
                {
                    new
                    {
                        title = $"Session: {bundle.SessionName}",
                        description = string.IsNullOrWhiteSpace(bundle.SummaryText) ? $"Outcome: {bundle.Outcome}" : $"```text\n{bundle.SummaryText}\n```",
                        color = bundle.Outcome.Contains("Caught", StringComparison.OrdinalIgnoreCase) ? 0x10B981 : 0xEF4444,
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            string jsonString = JsonSerializer.Serialize(payload);
            form.Add(new StringContent(jsonString, Encoding.UTF8, "application/json"), "payload_json");

            // Attach zip file (if under 25 MB Discord limit)
            var fileBytes = await File.ReadAllBytesAsync(bundle.ZipPath);
            if (fileBytes.Length <= 25 * 1024 * 1024)
            {
                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
                form.Add(fileContent, "file", Path.GetFileName(bundle.ZipPath));
            }

            var response = await _httpClient.PostAsync(webhookUrl, form);
            if (response.IsSuccessStatusCode)
            {
                return (true, "Diagnostic bundle uploaded to Discord successfully!");
            }
            else
            {
                string errorBody = await response.Content.ReadAsStringAsync();
                return (false, $"Discord error ({response.StatusCode}): {errorBody}");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Failed to send to Discord: {ex.Message}");
        }
    }
}
