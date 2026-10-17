using System.IO;
using System.Net.Http;
using Velopack;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.UI;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed class AppUpdaterTests
{
    [Fact]
    public void PatchNotes_AreBundledOffline_AndKeepTheStudioGuide()
    {
        var current = ReleaseNotesWindow.VersionNotes(AppUpdater.CurrentVersion.ToString(3));
        Assert.NotNull(current);
        Assert.Contains("Settings → App updates → What’s new", current);
        Assert.DoesNotContain("## [3.4.0]", current);
        var studio = ReleaseNotesWindow.VersionNotes("3.4.0");
        Assert.NotNull(studio);
        Assert.Contains("Customize wallpaper", studio);
        Assert.Contains("How slideshow and shuffle work", studio);
        Assert.Contains("Save skin", studio);
    }

    [Fact]
    public void PatchNotes_PreferTheAvailableUpdatesNotes_AndExplainMissingNotes()
    {
        const string future = "### New features\n\nNotes supplied with the future update.";
        Assert.Equal(future, ReleaseNotesWindow.UpdateNotes("9.0.0", future));
        Assert.Contains("Customize wallpaper", ReleaseNotesWindow.UpdateNotes("3.4.0", null));
        Assert.Contains("View on GitHub", ReleaseNotesWindow.UpdateNotes("9.0.0", ""));
        Assert.Null(ReleaseNotesWindow.VersionNotes("3.4.0.*"));
    }

    private static UpdateInfo Release(int patch = 1) => new(new VelopackAsset
    {
        PackageId = "Belie",
        Version = new SemanticVersion(99, 0, patch),
        Type = VelopackAssetType.Full,
        FileName = $"Belie-99.0.{patch}-full.nupkg",
    }, false, null, Array.Empty<VelopackAsset>());

    [Fact]
    public async Task UnpackagedBuild_ExplainsHowToEnableUpdatesWithoutCheckingFeed()
    {
        var client = new StubClient { IsInstalled = false };
        var updater = new AppUpdater(client);
        await updater.CheckAsync();
        Assert.Equal(0, client.Checks);
        Assert.Null(updater.Available);
        Assert.Contains("Setup.exe", updater.Status);
        Assert.False(updater.Busy);
    }

    [Fact]
    public void DevelopmentBuild_IsNotTreatedAsAVelopackInstallation()
    {
        VelopackApp.Build().SetArgs(Array.Empty<string>()).SetAutoApplyOnStartup(false).Run();
        Assert.False(new VelopackUpdateClient().IsInstalled);
    }

    [Fact]
    public async Task Check_NotifiesOncePerVersion_AndPreservesOfferOnNetworkFailure()
    {
        var client = new StubClient { Release = Release() };
        var updater = new AppUpdater(client);
        var notifications = 0;
        updater.UpdateFound += () => notifications++;
        await updater.CheckAsync();
        await updater.CheckAsync();
        Assert.Equal(1, notifications);
        client.Release = Release(2);
        await updater.CheckAsync();
        Assert.Equal(2, notifications);
        client.CheckError = new HttpRequestException("Rate limited");
        await updater.CheckAsync();
        Assert.False(updater.Busy);
        Assert.Contains("Couldn’t check", updater.Status);
        Assert.Same(client.Release, updater.Available);
        client.CheckError = null;
        client.Release = null;
        await updater.CheckAsync();
        Assert.Null(updater.Available);
        Assert.Contains("up to date", updater.Status);
    }

    [Fact]
    public async Task Install_DownloadsAndArmsUpdaterBeforeCleanShutdown()
    {
        var client = new StubClient { Release = Release() };
        var updater = new AppUpdater(client);
        await updater.CheckAsync();
        await updater.InstallAsync(() => { client.Events.Add("guard"); return true; },
            () => client.Events.Add("shutdown"), default);
        Assert.Equal(new[] { "download", "guard", "apply", "shutdown" }, client.Events);
        Assert.False(updater.Busy);
    }

    [Fact]
    public async Task UnsavedChanges_CanPostponeUpdate_AndRetryUsesSameRelease()
    {
        var client = new StubClient { Release = Release() };
        var updater = new AppUpdater(client);
        await updater.CheckAsync();
        await updater.InstallAsync(() => false, () => Assert.Fail("Must not exit"), default);
        Assert.Equal(new[] { "download" }, client.Events);
        Assert.Contains("postponed", updater.Status);
        Assert.Same(client.Release, updater.Available);
        await updater.InstallAsync(() => true, () => client.Events.Add("shutdown"), default);
        Assert.Equal(new[] { "download", "download", "apply", "shutdown" }, client.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownloadOrHandoffFailure_DoesNotShutDown(bool failHandoff)
    {
        var client = new StubClient { Release = Release(), FailDownload = !failHandoff, FailApply = failHandoff };
        var updater = new AppUpdater(client);
        await updater.CheckAsync();
        await updater.InstallAsync(() => true, () => Assert.Fail("Must not exit"), default);
        Assert.Contains("Couldn’t install", updater.Status);
        Assert.False(updater.Busy);
        Assert.Same(client.Release, updater.Available);
    }

    [Fact]
    public async Task CancellationAfterDownload_DoesNotApplyOrRestart()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new StubClient { Release = Release(), AfterDownload = cancellation.Cancel };
        var updater = new AppUpdater(client);
        await updater.CheckAsync();
        await updater.InstallAsync(() => throw new InvalidOperationException("Must not prompt"),
            () => Assert.Fail("Must not exit"), cancellation.Token);
        Assert.Equal(new[] { "download" }, client.Events);
        Assert.Contains("cancelled", updater.Status);
        Assert.False(updater.Busy);
    }

    [Fact]
    public async Task CheckAndInstall_CannotOverlap()
    {
        var completion = new TaskCompletionSource<UpdateInfo?>();
        var client = new StubClient { PendingCheck = completion.Task };
        var updater = new AppUpdater(client);
        var check = updater.CheckAsync();
        Assert.True(updater.Busy);
        await updater.CheckAsync();
        await updater.InstallAsync(() => true, () => Assert.Fail("Must not exit"), default);
        Assert.Equal(1, client.Checks);
        Assert.Empty(client.Events);
        completion.SetResult(Release());
        await check;
        Assert.False(updater.Busy);
    }

    private sealed class StubClient : IAppUpdateClient
    {
        public bool IsInstalled { get; set; } = true;
        public UpdateInfo? Release { get; set; }
        public Exception? CheckError { get; set; }
        public bool FailDownload { get; set; }
        public bool FailApply { get; set; }
        public Action? AfterDownload { get; set; }
        public Task<UpdateInfo?>? PendingCheck { get; set; }
        public int Checks { get; private set; }
        public List<string> Events { get; } = new();

        public Task<UpdateInfo?> CheckAsync()
        {
            Checks++;
            if (CheckError != null) throw CheckError;
            return PendingCheck ?? Task.FromResult(Release);
        }

        public Task DownloadAsync(UpdateInfo update, Action<int> progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Same(Release, update);
            Events.Add("download");
            if (FailDownload) throw new IOException("Package verification failed");
            AfterDownload?.Invoke();
            return Task.CompletedTask;
        }

        public void ApplyOnExit(UpdateInfo update)
        {
            Assert.Same(Release, update);
            Events.Add("apply");
            if (FailApply) throw new IOException("Could not start Update.exe");
        }
    }
}
