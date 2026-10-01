using System.IO;
using System.Text.Json;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed class LibraryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "belie-library-tests-" + Guid.NewGuid().ToString("N"));
    private string LibraryPath => Path.Combine(_dir, "library.json");

    public LibraryStoreTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void Import_FolderAndRepeatedFiles_DeduplicatesWithoutChangingMetadata()
    {
        var image = Path.Combine(_dir, "forest.png");
        var video = Path.Combine(_dir, "rain.mp4");
        File.WriteAllText(image, "image fixture");
        File.WriteAllText(video, "video fixture");
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "unsupported");
        var subfolder = Directory.CreateDirectory(Path.Combine(_dir, "nested")).FullName;
        File.WriteAllText(Path.Combine(subfolder, "nested.jpg"), "nested fixture");
        var existing = new WallpaperAsset { FilePath = image, Name = "My forest", IsFavorite = true, Tags = new() { "calm" } };

        var added = LibraryStore.Import(new[] { existing }, new[] { _dir, video, image.ToUpperInvariant() });

        Assert.Equal(video, Assert.Single(added).FilePath);
        Assert.Equal("My forest", existing.Name);
        Assert.True(existing.IsFavorite);
        Assert.Equal("calm", Assert.Single(existing.Tags));
    }

    [Fact]
    public void SaveAndReopen_PreservesOrganizationAndMissingSources()
    {
        var store = new LibraryStore(LibraryPath);
        var missing = Path.Combine(_dir, "disconnected", "night.jpg");
        store.Save(new[] { new WallpaperAsset
        {
            FilePath = missing, Name = "Evening", IsFavorite = true,
            Collection = "Wind down", Tags = new() { "night", "city" }
        } });

        var loaded = Assert.Single(new LibraryStore(LibraryPath).Load());

        Assert.Equal(missing, loaded.FilePath);
        Assert.Equal("Evening", loaded.Name);
        Assert.True(loaded.IsFavorite);
        Assert.Equal("Wind down", loaded.Collection);
        Assert.Equal(new[] { "night", "city" }, loaded.Tags);
        Assert.False(File.Exists(missing));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Load_CorruptLibrary_ReportsFailureAndPreservesOriginal()
    {
        const string corrupt = "{ broken library";
        File.WriteAllText(LibraryPath, corrupt);

        Assert.Throws<JsonException>(() => new LibraryStore(LibraryPath).Load());

        Assert.Equal(corrupt, File.ReadAllText(LibraryPath));
    }

    [Fact]
    public void Load_NormalizesMetadataAndDeduplicatesPaths()
    {
        var path = Path.Combine(_dir, "forest.png");
        var store = new LibraryStore(LibraryPath);
        store.Save(new[]
        {
            new WallpaperAsset { FilePath = path, Name = null!, Collection = null!, Tags = new() { " calm ", "CALM", "", null! } },
            new WallpaperAsset { FilePath = path.ToUpperInvariant(), Name = "Duplicate" }
        });

        var asset = Assert.Single(store.Load());

        Assert.Equal("forest", asset.Name);
        Assert.Equal("", asset.Collection);
        Assert.Equal("calm", Assert.Single(asset.Tags));
    }

    [Fact]
    public void Save_WhenDestinationCannotBeReplaced_PreservesPreviousLibrary()
    {
        var store = new LibraryStore(LibraryPath);
        store.Save(new[] { new WallpaperAsset { FilePath = Path.Combine(_dir, "a.jpg"), Name = "Saved" } });
        var before = File.ReadAllText(LibraryPath);
        using (File.Open(LibraryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => store.Save(Array.Empty<WallpaperAsset>()));
            Assert.True(error is IOException or UnauthorizedAccessException);
        }

        Assert.Equal(before, File.ReadAllText(LibraryPath));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
