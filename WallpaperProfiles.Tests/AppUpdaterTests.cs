using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WallpaperProfiles.Infrastructure;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed class AppUpdaterTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("test release payload");
    private static string Hash => Convert.ToHexString(SHA256.HashData(Payload));

    private static string Release(string tag = "v3.2.0", bool draft = false, bool prerelease = false,
        string? digest = null, string? url = null, string name = "Belie.exe") => JsonSerializer.Serialize(new
    {
        tag_name = tag, draft, prerelease,
        assets = new[] { new { name, state = "uploaded", size = Payload.Length,
            digest = digest ?? "sha256:" + Hash,
            browser_download_url = url ?? $"https://github.com/{AppUpdater.Repository}/releases/download/{tag}/Belie.exe" } }
    });

    [Fact]
    public void NewStableRelease_SelectsVerifiedExecutable()
    {
        var release = AppUpdater.ParseRelease(Release(), new Version(3, 1, 1));
        Assert.NotNull(release);
        Assert.Equal(new Version(3, 2, 0, 0), release.Version);
        Assert.Equal(Payload.Length, release.Size);
        Assert.Equal(Hash, release.Sha256);
    }

    [Theory]
    [InlineData("v3.1.1")]
    [InlineData("3.1.1.0")]
    [InlineData("v3.0.0")]
    [InlineData("v3.2.0-beta.1")]
    [InlineData("latest")]
    public void EqualOlderAndNonStableTags_AreNotUpdates(string tag) =>
        Assert.Null(AppUpdater.ParseRelease(Release(tag), new Version(3, 1, 1, 0)));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void UnpublishedReleases_AreIgnored(bool draft, bool prerelease) =>
        Assert.Null(AppUpdater.ParseRelease(Release(draft: draft, prerelease: prerelease), new Version(3, 1, 1)));

    [Theory]
    [InlineData("http://github.com/el-rato/Wallpaper-Profile-App/releases/download/v3.2.0/Belie.exe")]
    [InlineData("https://example.com/Belie.exe")]
    [InlineData("https://github.com/other/repo/releases/download/v3.2.0/Belie.exe")]
    public void UnexpectedDownloadLocations_AreRejected(string url) =>
        Assert.Throws<InvalidDataException>(() => AppUpdater.ParseRelease(Release(url: url), new Version(3, 1, 1)));

    [Fact]
    public void MissingExecutableOrHash_IsNotReportedAsUpToDate()
    {
        Assert.Throws<InvalidDataException>(() => AppUpdater.ParseRelease(Release(name: "source.zip"), new Version(3, 1, 1)));
        Assert.Throws<InvalidDataException>(() => AppUpdater.ParseRelease(Release(digest: ""), new Version(3, 1, 1)));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public async Task Download_VerifiesHashAndSize_AndRemovesBadDownloads(int sizeOffset, bool wrongHash)
    {
        var path = Path.Combine(Path.GetTempPath(), "belie-test-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(Payload) }));
            var updater = new AppUpdater(http);
            var release = AppUpdater.ParseRelease(Release(), new Version(3, 1, 1))!;
            release = release with { Size = Payload.Length + sizeOffset, Sha256 = wrongHash ? new string('0', 64) : Hash };
            if (sizeOffset != 0 || wrongHash)
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => updater.DownloadAsync(release, path, null, default));
                Assert.False(File.Exists(path));
            }
            else
            {
                await updater.DownloadAsync(release, path, null, default);
                Assert.Equal(Payload, File.ReadAllBytes(path));
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Check_HandlesRateLimitsAndNotifiesOnlyOncePerVersion()
    {
        var fail = false;
        using var http = new HttpClient(new StubHandler(request =>
        {
            Assert.Contains("Belie/", request.Headers.UserAgent.ToString());
            return fail ? new HttpResponseMessage(HttpStatusCode.Forbidden) : new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(Release("v99.0.0")) };
        }));
        var updater = new AppUpdater(http);
        var notifications = 0;
        updater.UpdateFound += () => notifications++;
        await updater.CheckAsync();
        await updater.CheckAsync();
        Assert.Equal(1, notifications);
        Assert.NotNull(updater.Available);
        fail = true;
        await updater.CheckAsync();
        Assert.False(updater.Busy);
        Assert.Contains("Couldn’t check", updater.Status);
        Assert.NotNull(updater.Available);
    }

    [Fact]
    public void Cleanup_LeavesUnrelatedFilesAlone()
    {
        var job = Path.Combine(Path.GetTempPath(), "belie-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        try
        {
            File.WriteAllText(Path.Combine(job, "download.exe"), "partial");
            File.WriteAllText(Path.Combine(job, "user-data.txt"), "keep");
            AppUpdater.CleanupJob(job);
            Assert.False(File.Exists(Path.Combine(job, "download.exe")));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(job, "user-data.txt")));
        }
        finally { File.Delete(Path.Combine(job, "user-data.txt")); Directory.Delete(job); }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
