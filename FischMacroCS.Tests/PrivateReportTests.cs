using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FischMacroCS.Core;

namespace FischMacroCS.Tests;

public class PrivateReportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fisch-http-" + Guid.NewGuid().ToString("N"));
    private string Zip => Path.Combine(_directory, "report.zip");
    public PrivateReportTests() { Directory.CreateDirectory(_directory); File.WriteAllText(Zip, "frozen report"); }
    public void Dispose() => Directory.Delete(_directory, true);
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task PublicDestinationIsRejectedBeforeAnyUpload()
    {
        int calls = 0;
        using var http = new HttpClient(new Handler(request => { calls++; Assert.Equal(HttpMethod.Get, request.Method); return Json("{\"private\":false}"); }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PrivateReportClient(http).SendAsync("owner/public", "fake", Zip, ""));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SuccessfulRetryUsesSavedAssetAndIssue()
    {
        int uploads = 0, issues = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            string path = request.RequestUri!.AbsolutePath;
            if (request.RequestUri.Host == "uploads.github.com") { uploads++; return Json("{\"browser_download_url\":\"https://github.com/private/report.zip\"}"); }
            if (path.EndsWith("/issues")) { issues++; return Json("{\"html_url\":\"https://github.com/private/issues/1\"}"); }
            if (path.EndsWith("/assets")) return Json("[]");
            if (path.EndsWith("/diagnostics")) return Json("{\"id\":1}");
            return Json("{\"private\":true}");
        }));
        var client = new PrivateReportClient(http);
        string first = await client.SendAsync("owner/private", "fake", Zip, "first");
        Assert.Equal(first, await client.SendAsync("owner/private", "fake", Zip, "retry"));
        Assert.Equal(1, uploads); Assert.Equal(1, issues);
    }

    [Fact]
    public async Task LostIssueResponseReconcilesWithoutAnotherPost()
    {
        int posts = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.RequestUri.Host == "uploads.github.com") return Json("{\"browser_download_url\":\"https://github.com/private/report.zip\"}");
            if (path.EndsWith("/issues"))
            {
                if (request.Method == HttpMethod.Post) { posts++; throw new HttpRequestException("Response lost"); }
                var progress = JsonSerializer.Deserialize<PrivateReportClient.Progress>(File.ReadAllText(Zip + ".submission.json"))!;
                return Json(JsonSerializer.Serialize(new[] { new { body = "<!-- fisch-report:" + progress.Id + " -->", html_url = "https://github.com/private/issues/1" } }));
            }
            if (path.EndsWith("/assets")) return Json("[]");
            if (path.EndsWith("/diagnostics")) return Json("{\"id\":1}");
            return Json("{\"private\":true}");
        }));
        var client = new PrivateReportClient(http);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync("owner/private", "fake", Zip, ""));
        Assert.Equal("https://github.com/private/issues/1", await client.SendAsync("owner/private", "fake", Zip, ""));
        Assert.Equal(1, posts);
    }

    [Fact]
    public async Task RejectedIssueCanRetryAfterCredentialsAreFixed()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.RequestUri.Host == "uploads.github.com") return Json("{\"browser_download_url\":\"https://github.com/private/report.zip\"}");
            if (path.EndsWith("/issues")) return Json("{}", HttpStatusCode.Forbidden);
            if (path.EndsWith("/assets")) return Json("[]");
            if (path.EndsWith("/diagnostics")) return Json("{\"id\":1}");
            return Json("{\"private\":true}");
        }));
        await Assert.ThrowsAsync<HttpRequestException>(() => new PrivateReportClient(http).SendAsync("owner/private", "fake", Zip, ""));
        var state = JsonSerializer.Deserialize<PrivateReportClient.Progress>(File.ReadAllText(Zip + ".submission.json"))!;
        Assert.False(state.IssueRequestStarted); Assert.NotNull(state.AssetUrl);
    }
}
