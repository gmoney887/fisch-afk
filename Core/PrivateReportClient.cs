using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace FischMacroCS.Core;

public static class ReportCredentials
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Write(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Read(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr buffer);
    public static void Save(string repository, string token)
    {
        IntPtr blob = Marshal.StringToCoTaskMemUni(token);
        try
        {
            var credential = new Credential { Type = 1, TargetName = "FischAFKPro/" + repository,
                Blob = blob, BlobSize = (uint)Encoding.Unicode.GetByteCount(token), Persist = 2, UserName = "github-token" };
            if (!Write(ref credential, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.ZeroFreeCoTaskMemUnicode(blob); }
    }
    public static string? Load(string repository)
    {
        if (!Read("FischAFKPro/" + repository, 1, 0, out var pointer)) return null;
        try { var credential = Marshal.PtrToStructure<Credential>(pointer); return Marshal.PtrToStringUni(credential.Blob, (int)credential.BlobSize / 2); }
        finally { CredFree(pointer); }
    }
}

public sealed class PrivateReportClient(HttpClient client)
{
    public sealed record Progress(string Id, string Repository, string Hash, string? AssetUrl = null,
        string? IssueUrl = null, bool IssueRequestStarted = false);
    public async Task<string> SendAsync(string repository, string token, string zipPath, string description, CancellationToken ct = default)
    {
        if (!Regex.IsMatch(repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) throw new ArgumentException("Use owner/repository for the private destination.");
        if (new FileInfo(zipPath).Length > 20L * 1024 * 1024) throw new InvalidOperationException("Upload exceeds 20 MB. Select the reduced bundle.");
        string hash;
        await using (var stream = File.OpenRead(zipPath)) hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
        string statePath = zipPath + ".submission.json";
        Progress? progress = File.Exists(statePath) ? JsonSerializer.Deserialize<Progress>(await File.ReadAllTextAsync(statePath, ct)) : null;
        if (progress == null || progress.Repository != repository || progress.Hash != hash) progress = new(Guid.NewGuid().ToString("N"), repository, hash);
        async Task Save() { await File.WriteAllTextAsync(statePath + ".tmp", JsonSerializer.Serialize(progress), ct); File.Move(statePath + ".tmp", statePath, true); }
        async Task<JsonDocument> Request(HttpMethod method, string url, HttpContent? content = null)
        {
            using var request = new HttpRequestMessage(method, url) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.UserAgent.ParseAdd("FischAFKPro-Diagnostics/2");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "GitHub credentials expired or are invalid. Replace the repository token.",
                    HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests => "GitHub denied the request or rate limited it. Progress is saved; check token permissions and retry later.",
                    _ => $"GitHub returned {(int)response.StatusCode}. Progress is saved for retry."
                }, null, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        string api = "https://api.github.com/repos/" + repository;
        using (var repo = await Request(HttpMethod.Get, api))
            if (!repo.RootElement.GetProperty("private").GetBoolean()) throw new InvalidOperationException("Diagnostics destination must be a private repository.");
        if (progress.IssueUrl != null) return progress.IssueUrl;
        await Save();
        using var release = await Request(HttpMethod.Get, api + "/releases/tags/diagnostics");
        long releaseId = release.RootElement.GetProperty("id").GetInt64();
        string name = "report-" + progress.Id + ".zip";
        if (progress.AssetUrl == null)
        {
            // A deterministic name recovers an upload that succeeded before the response or local save was lost.
            for (int page = 1; ; page++)
            {
                using var assets = await Request(HttpMethod.Get, $"{api}/releases/{releaseId}/assets?per_page=100&page={page}");
                foreach (var asset in assets.RootElement.EnumerateArray())
                    if (asset.GetProperty("name").GetString() == name && asset.GetProperty("state").GetString() == "uploaded")
                        progress = progress with { AssetUrl = asset.GetProperty("browser_download_url").GetString() };
                if (progress.AssetUrl != null || assets.RootElement.GetArrayLength() < 100) break;
            }
            if (progress.AssetUrl == null)
            {
                await using var file = File.OpenRead(zipPath);
                using var body = new StreamContent(file);
                body.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                using var uploaded = await Request(HttpMethod.Post, $"https://uploads.github.com/repos/{repository}/releases/{releaseId}/assets?name={name}", body);
                progress = progress with { AssetUrl = uploaded.RootElement.GetProperty("browser_download_url").GetString() };
            }
            await Save();
        }
        string marker = "<!-- fisch-report:" + progress.Id + " -->";
        if (progress.IssueRequestStarted)
        {
            for (int page = 1; ; page++)
            {
                using var issues = await Request(HttpMethod.Get, $"{api}/issues?state=all&per_page=100&page={page}");
                foreach (var issue in issues.RootElement.EnumerateArray())
                {
                    if (!issue.TryGetProperty("pull_request", out _) && issue.TryGetProperty("body", out var issueBody) &&
                        (issueBody.GetString() ?? "").Contains(marker, StringComparison.Ordinal))
                    {
                        progress = progress with { IssueUrl = issue.GetProperty("html_url").GetString() };
                        await Save(); return progress.IssueUrl!;
                    }
                }
                if (issues.RootElement.GetArrayLength() < 100) break;
            }
            throw new InvalidOperationException("The previous issue request has an uncertain result. The uploaded asset is saved; check the private repository before retrying. No duplicate issue was created.");
        }
        progress = progress with { IssueRequestStarted = true };
        await Save();
        var payload = new { title = "Gameplay diagnostic " + progress.Id,
            body = RecordingSubmissionService.SanitizeText(description) + "\n\n[Diagnostic bundle](" + progress.AssetUrl + ")\n\n" + marker };
        try
        {
            using var created = await Request(HttpMethod.Post, api + "/issues", new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            progress = progress with { IssueUrl = created.RootElement.GetProperty("html_url").GetString() };
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or
            HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity or HttpStatusCode.TooManyRequests)
        {
            // These responses explicitly reject creation. A corrected token/rate-limit retry is safe.
            progress = progress with { IssueRequestStarted = false };
            await Save();
            throw;
        }
        await Save(); return progress.IssueUrl!;
    }
}
