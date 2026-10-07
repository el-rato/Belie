using System.IO;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.Engine;
using System.Reflection;
using System.Collections.Concurrent;
using Xunit;

namespace WallpaperProfiles.Tests;

public class ProfileStoreTests : IDisposable
{
    private readonly string _dir;

    public ProfileStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "wpstore-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
        }
    }

    private ProfileStore CreateStore() => new(_dir);

    [Fact]
    public void SaveThenLoad_RoundTripsProfile()
    {
        var store = CreateStore();
        var profile = new WallpaperProfile
        {
            Name = "Work",
            FolderPath = @"C:\Images\Work",
            AdditionalWallpaperPaths = new() { @"D:\Wallpapers\forest.png", @"C:\Downloads\sky.jpg" },
            FitMode = FitMode.Span,
            SlideshowIntervalMinutes = 15,
            SlideshowRandom = true,
            Schedule =
            [
                new ScheduleRule
                {
                    StartTime = new TimeOnly(9, 0),
                    EndTime = new TimeOnly(17, 0),
                    DaysOfWeek = new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Friday },
                },
            ],
            EventTriggers = [new EventTrigger { Type = TriggerType.IdleForMinutes, Condition = "15", Priority = 7 }],
        };

        store.Save(profile);
        var loaded = Assert.Single(store.LoadAll());

        Assert.Equal(profile.Id, loaded.Id);
        Assert.Equal("Work", loaded.Name);
        Assert.Equal(profile.AdditionalWallpaperPaths, loaded.AdditionalWallpaperPaths);
        Assert.Equal(FitMode.Span, loaded.FitMode);
        Assert.Equal(15, loaded.SlideshowIntervalMinutes);
        Assert.True(loaded.SlideshowRandom);
        Assert.Equal(new TimeOnly(9, 0), loaded.Schedule[0].StartTime);
        Assert.Equal("15", loaded.EventTriggers[0].Condition);
        Assert.Equal(7, loaded.EventTriggers[0].Priority);
        // No temp file left behind after an atomic save.
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Load_QuarantinesCorruptFile_AndSkipsIt()
    {
        var store = CreateStore();
        var good = new WallpaperProfile { Name = "Good", FolderPath = @"C:\Images" };
        store.Save(good);
        File.WriteAllText(Path.Combine(_dir, "broken.json"), "{ this is not json");

        var loaded = store.LoadAll();

        var profile = Assert.Single(loaded);
        Assert.Equal("Good", profile.Name);
        // The corrupt file was renamed out of the way, not left as a loadable .json.
        Assert.Empty(Directory.GetFiles(_dir, "broken.json"));
        Assert.Single(Directory.GetFiles(_dir, "broken.json.corrupt-*"));
    }

    [Fact]
    public void Load_NormalizesOutOfRangeValues()
    {
        var store = CreateStore();
        var extreme = new WallpaperProfile
        {
            Name = "Extreme",
            FolderPath = @"C:\Images",
            SlideshowIntervalMinutes = 99_999,
        };
        store.Save(extreme);

        var loaded = Assert.Single(store.LoadAll());

        Assert.Equal(1440, loaded.SlideshowIntervalMinutes);
    }

    [Fact]
    public void Delete_RemovesProfileFile()
    {
        var store = CreateStore();
        var profile = new WallpaperProfile { Name = "Doomed", FolderPath = @"C:\Images" };
        store.Save(profile);

        Assert.True(store.Delete(profile.Id));
        Assert.False(store.Delete(profile.Id));
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void ProfileMedia_CombinesSourcesAcrossFolders_WithoutDuplicatesOrMissingFiles()
    {
        var folder = Path.Combine(_dir, "nature");
        var other = Path.Combine(_dir, "downloads");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(other);
        var first = Path.Combine(folder, "forest.png");
        var second = Path.Combine(other, "sky.jpg");
        var video = Path.Combine(other, "ocean.mp4");
        File.WriteAllBytes(first, new byte[] { 1 });
        File.WriteAllBytes(second, new byte[] { 2 });
        File.WriteAllBytes(video, new byte[] { 3 });
        var profile = new WallpaperProfile { FolderPath = folder,
            AdditionalWallpaperPaths = new() { first, second, second.ToUpperInvariant(), video, Path.Combine(other, "missing.png") } };
        CreateStore().Save(profile);
        var loaded = Assert.Single(CreateStore().LoadAll());
        Assert.Equal(new[] { first, second, video }, WallpaperEngine.GetProfileMedia(loaded));
        Assert.Contains(Path.Combine(other, "missing.png"), loaded.AdditionalWallpaperPaths);
    }

    [Fact]
    public async Task Slideshow_RotatesWallpaperList_AndSkipsRemovedFiles()
    {
        var first = Path.Combine(_dir, "forest.png");
        var folder = Path.Combine(_dir, "other-folder");
        Directory.CreateDirectory(folder);
        var second = Path.Combine(folder, "sky.jpg");
        var video = Path.Combine(folder, "ocean.mp4");
        foreach (var path in new[] { first, second, video }) File.WriteAllBytes(path, new byte[] { 1 });
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = new ConcurrentQueue<string>();
        using var slideshow = new SlideshowController((path, _) => { applied.Enqueue(path); ready.TrySetResult(); });
        slideshow.Start(new WallpaperProfile { FolderPath = first, AdditionalWallpaperPaths = new() { second, video }, SlideshowIntervalMinutes = 5 });
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        async Task Advance() => await (Task)typeof(SlideshowController)
            .GetMethod("AdvanceAndApplyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(slideshow, null)!;
        await Advance();
        await Advance();
        File.Delete(second);
        await Advance();
        Assert.Equal(new[] { first, second, video, first }, applied.ToArray());
    }

    [Fact]
    public void LegacyProfile_AndNullWallpaperList_LoadWithoutChangingSource()
    {
        var id = Guid.NewGuid();
        File.WriteAllText(Path.Combine(_dir, id + ".json"), $$"""{"Id":"{{id}}","Name":"Legacy","FolderPath":"old-folder","AdditionalWallpaperPaths":null}""");
        var profile = Assert.Single(CreateStore().LoadAll());
        Assert.Empty(profile.AdditionalWallpaperPaths);
        Assert.Equal("old-folder", profile.FolderPath);
    }
}
