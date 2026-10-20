using System.Diagnostics;
using System.IO;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.Resolution;
using Xunit;
using Xunit.Abstractions;

namespace WallpaperProfiles.Tests;

/// <summary>Bounded load tests using disposable data; never applies a desktop wallpaper.</summary>
[Trait("Category", "Stress")]
public sealed class StressTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "belie-stress-" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;

    public StressTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Settings_ConcurrentSavesAndReads_PublishCompleteSnapshots()
    {
        var path = Path.Combine(_directory, "settings.json");
        var clock = Stopwatch.StartNew();
        RunWorkers(worker =>
        {
            var store = new SettingsStore(path);
            for (var index = 0; index < 50; index++)
            {
                var token = $"{worker}:{index}";
                store.Save(new AppSettings { PreferredProfileId = token, LastActiveProfileId = token });
                // Parse the raw file too: Load's default fallback must not hide a failed read.
                var raw = AtomicJsonFile.Read<AppSettings>(path)!;
                Assert.False(string.IsNullOrEmpty(raw.PreferredProfileId));
                Assert.Equal(raw.PreferredProfileId, raw.LastActiveProfileId);
                var loaded = store.Load();
                Assert.False(string.IsNullOrEmpty(loaded.PreferredProfileId));
                Assert.Equal(loaded.PreferredProfileId, loaded.LastActiveProfileId);
            }
        });
        Report(clock, "400 settings saves with concurrent readers");
    }

    [Fact]
    public void Profiles_ConcurrentReplacement_DoesNotQuarantineValidData()
    {
        var id = Guid.NewGuid();
        var clock = Stopwatch.StartNew();
        RunWorkers(worker =>
        {
            var store = new ProfileStore(_directory);
            for (var index = 0; index < 25; index++)
            {
                var token = $"{worker}:{index}";
                store.Save(new WallpaperProfile { Id = id, Name = token, FolderPath = token });
                var loaded = Assert.Single(store.LoadAll());
                Assert.Equal(id, loaded.Id);
                Assert.Equal(loaded.Name, loaded.FolderPath);
            }
        });
        Assert.Empty(Directory.GetFiles(_directory, "*.corrupt-*"));
        Report(clock, "200 profile replacements with concurrent readers");
    }

    [Fact]
    public void Library_ConcurrentReplacement_KeepsEverySnapshotConsistent()
    {
        var path = Path.Combine(_directory, "library.json");
        var clock = Stopwatch.StartNew();
        RunWorkers(worker =>
        {
            var store = new LibraryStore(path);
            for (var index = 0; index < 25; index++)
            {
                var token = $"{worker}:{index}";
                store.Save(Enumerable.Range(0, 64).Select(item => new WallpaperAsset
                {
                    FilePath = Path.Combine(_directory, $"{item}.jpg"), Name = token,
                    Tags = new() { token }, Collection = token
                }));
                var loaded = store.Load();
                Assert.Equal(64, loaded.Count);
                Assert.All(loaded, asset =>
                {
                    Assert.Equal(loaded[0].Name, asset.Name);
                    Assert.Equal(asset.Name, asset.Collection);
                    Assert.Equal(asset.Name, Assert.Single(asset.Tags));
                });
            }
        });
        Report(clock, "200 library replacements of 64 assets with concurrent readers");
    }

    [Fact]
    public void Canvas_ConcurrentUpdatesAcrossStoreInstances_DoNotLoseEdits()
    {
        var path = Path.Combine(_directory, "canvas.json");
        var widgets = Enumerable.Range(0, 8).Select(_ => new DesktopWidget()).ToList();
        new DesktopCanvasStore(path).Save(new DesktopCanvasDocument { Widgets = widgets });
        var clock = Stopwatch.StartNew();
        RunWorkers(worker =>
        {
            var store = new DesktopCanvasStore(path);
            for (var index = 0; index < 50; index++)
            {
                store.Update(document =>
                {
                    document.Widgets[worker].Content += "x";
                    document.Widgets[worker].X += 1;
                });
                Assert.Equal(8, store.Load().Widgets.Count);
            }
        });
        Assert.All(new DesktopCanvasStore(path).Load().Widgets, widget =>
        {
            Assert.Equal(new string('x', 50), widget.Content);
            Assert.Equal(210, widget.X);
        });
        Report(clock, "400 locked canvas updates across 8 store instances");
    }

    [Fact]
    public void Library_LargeSnapshot_NormalizesDuplicatesAndPreservesMetadata()
    {
        var store = new LibraryStore(Path.Combine(_directory, "library.json"));
        var assets = Enumerable.Range(0, 10000).Select(index => new WallpaperAsset
        {
            FilePath = Path.Combine(_directory, $"{index}.jpg"), Name = $"Wallpaper {index}",
            Tags = new() { " night ", "NIGHT", "city" }, IsFavorite = index % 2 == 0
        }).ToArray();
        var clock = Stopwatch.StartNew();
        store.Save(assets.Concat(assets));
        var loaded = store.Load();
        Assert.Equal(assets.Length, loaded.Count);
        for (var index = 0; index < loaded.Count; index++)
        {
            Assert.Equal(assets[index].Name, loaded[index].Name);
            Assert.Equal(assets[index].IsFavorite, loaded[index].IsFavorite);
            Assert.Equal(new[] { "night", "city" }, loaded[index].Tags);
        }
        Report(clock, "20,000 library records reduced to 10,000 unique assets");
    }

    [Fact]
    public void Canvas_ThousandsOfWidgets_KeepProfileAndWallpaperScopesIndependent()
    {
        var store = new DesktopCanvasStore(Path.Combine(_directory, "canvas.json"));
        var document = new DesktopCanvasDocument { Enabled = true };
        var profiles = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToArray();
        document.Widgets.Add(new DesktopWidget { Title = "Global" });
        foreach (var id in profiles)
        {
            document.Widgets.AddRange(Enumerable.Range(0, 16).Select(index => new DesktopWidget
            {
                ProfileId = id, Title = $"{id}:{index}", Content = "Preserve me"
            }));
            for (var index = 0; index < 3; index++) document.CustomizeWallpaper(id, $"{index}.jpg");
        }
        var clock = Stopwatch.StartNew();
        store.Save(document);
        var loaded = store.Load();
        Assert.Equal(3201, loaded.Widgets.Count);
        Assert.Equal(loaded.Widgets.Count, loaded.Widgets.Select(widget => widget.Id).Distinct().Count());
        foreach (var id in profiles)
        {
            for (var index = 0; index < 3; index++)
            {
                loaded.ActiveProfileId = id;
                loaded.ActiveWallpaperPath = $"{index}.jpg";
                var visible = loaded.VisibleWidgets().ToArray();
                Assert.Equal(17, visible.Length);
                Assert.All(visible.Where(widget => widget.ProfileId != null), widget =>
                {
                    Assert.Equal(id, widget.ProfileId);
                    Assert.Equal($"{index}.jpg", widget.WallpaperPath);
                    Assert.Equal("Preserve me", widget.Content);
                });
            }
            loaded.ResetWallpaper(id, "0.jpg");
            Assert.Equal(16, loaded.Composition(id, "0.jpg", false).Count());
        }
        Assert.Equal(3201, document.Widgets.Count);
        Report(clock, "3,201 widgets, 150 wallpaper layouts, and 50 independent resets");
    }

    [Fact]
    public void Resolver_ThousandsOfProfilesAndEvents_PreservesPriorityAndTieOrder()
    {
        var profiles = Enumerable.Range(0, 2000).Select(_ => new WallpaperProfile()).ToArray();
        var since = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        var random = new Random(42);
        var events = Enumerable.Range(0, 4000).Select(index => new ActiveEventSnapshot(
            index % 5 == 0 ? Guid.NewGuid() : profiles[random.Next(profiles.Length)].Id,
            new(), random.Next(100), since.AddSeconds(random.Next(100)))).ToList();
        // Equal priority and timestamp must keep the first event, while orphan events are ignored.
        events.Add(new(profiles[0].Id, new(), 100, since));
        events.Add(new(profiles[1].Id, new(), 100, since));
        events.Add(new(Guid.NewGuid(), new(), int.MaxValue, since));
        var ids = profiles.Select(profile => profile.Id).ToHashSet();
        var expected = events.Where(item => ids.Contains(item.ProfileId)).OrderByDescending(item => item.Priority)
            .ThenByDescending(item => item.SinceUtc).First();
        var clock = Stopwatch.StartNew();
        var resolver = new ProfileResolver();
        for (var index = 0; index < 250; index++)
        {
            var decision = resolver.Resolve(since, profiles, events);
            Assert.Equal(expected.ProfileId, decision.ProfileId);
            Assert.Equal(ResolutionSource.Event, decision.Source);
        }
        Report(clock, "250 resolutions of 2,000 profiles and 4,003 events");
    }

    [Fact]
    public void ScheduleBoundaries_MatchMinuteByMinuteReferenceAcrossRandomizedWeeks()
    {
        var random = new Random(1729);
        var clock = Stopwatch.StartNew();
        for (var sample = 0; sample < 48; sample++)
        {
            var rules = Enumerable.Range(0, 24).Select(_ => new ScheduleRule
            {
                StartTime = new TimeOnly(random.Next(24), 0),
                EndTime = new TimeOnly(random.Next(24), 0),
                DaysOfWeek = Enum.GetValues<DayOfWeek>().Where(_ => random.Next(4) == 0).ToHashSet()
            }).ToList();
            var profiles = new[] { new WallpaperProfile { Schedule = rules } };
            var after = new DateTime(2026, 10, 19).AddHours(random.Next(168));
            DateTime? expected = null;
            for (var minute = 1; minute <= 8 * 24 * 60; minute++)
            {
                var transition = after.AddMinutes(minute);
                var before = transition.AddTicks(-1);
                if (!rules.Any(rule => rule.Matches(before.DayOfWeek, TimeOnly.FromDateTime(before))
                    != rule.Matches(transition.DayOfWeek, TimeOnly.FromDateTime(transition)))) continue;
                expected = transition;
                break;
            }
            Assert.Equal(expected, ProfileResolver.NextBoundary(after, profiles));
        }
        Report(clock, "48 randomized schedule sets checked against a minute-by-minute reference");
    }

    [Fact]
    public void AtomicSave_SerializationFailure_PreservesPreviousFileAndCleansTemporaryFiles()
    {
        var path = Path.Combine(_directory, "snapshot.json");
        AtomicJsonFile.Write(path, new[] { 1, 2, 3 });
        var original = File.ReadAllBytes(path);
        Assert.Throws<InvalidOperationException>(() => AtomicJsonFile.Write(path, FailingSequence()));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));

        static IEnumerable<int> FailingSequence()
        {
            yield return 4;
            throw new InvalidOperationException("Simulated failure after writing has started.");
        }
    }

    [Fact]
    public void AtomicSave_TemporaryExternalReadLock_PublishesAfterRelease()
    {
        var path = Path.Combine(_directory, "locked-snapshot.json");
        AtomicJsonFile.Write(path, new[] { 1, 2, 3 });
        var held = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var save = Task.Run(() => AtomicJsonFile.Write(path, new[] { 4, 5, 6 }));
        try
        {
            Assert.True(SpinWait.SpinUntil(() => Directory.GetFiles(_directory, "*.tmp").Length > 0,
                TimeSpan.FromSeconds(5)));
            Assert.False(save.IsCompleted);
            Assert.Equal(new[] { 1, 2, 3 }, System.Text.Json.JsonSerializer.Deserialize<int[]>(held));
        }
        finally { held.Dispose(); }
        save.GetAwaiter().GetResult();
        Assert.Equal(new[] { 4, 5, 6 }, AtomicJsonFile.Read<int[]>(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    private static void RunWorkers(Action<int> worker)
    {
        try { Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, worker); }
        catch (AggregateException error) { throw new Exception(error.ToString(), error); }
    }

    private void Report(Stopwatch clock, string scenario)
    {
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        _output.WriteLine($"{scenario}: {clock.Elapsed.TotalMilliseconds:N0} ms");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
