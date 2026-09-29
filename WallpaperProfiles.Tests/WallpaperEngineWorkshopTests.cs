using System.IO;
using WallpaperProfiles.Engine;
using Xunit;

namespace WallpaperProfiles.Tests;

public class WallpaperEngineWorkshopTests : IDisposable
{
    private readonly string _root;

    public WallpaperEngineWorkshopTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "weworkshop-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private string WriteItem(string id, string json, params (string RelPath, byte[] Bytes)[] files)
    {
        var dir = Path.Combine(_root, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "project.json"), json);
        foreach (var (relPath, bytes) in files)
        {
            var path = Path.Combine(dir, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        return dir;
    }

    [Fact]
    public void ListItems_ParsesVideoSceneAndTitleFallback()
    {
        var videoDir = WriteItem("111111111",
            """{"contenttype":"video","file":"videos/ocean.mp4","title":"Ocean Waves","general":{"supportsaudio":true}}""",
            ("videos/ocean.mp4", new byte[] { 1, 2, 3 }),
            ("preview.jpg", new byte[] { 1, 2, 3 }));
        var sceneDir = WriteItem("222222222",
            """{"contenttype":"scene","file":"scene.pkg"}""",
            ("scene.pkg", new byte[] { 4, 5 }));
        var untitledDir = WriteItem("333333333",
            """{"contenttype":"video","file":"clip.mp4"}""",
            ("clip.mp4", new byte[] { 6 }));

        var items = WallpaperEngineWorkshop.ListItems(_root);

        Assert.Equal(3, items.Count);

        var video = items.Single(i => i.ContentType == "video" && i.Title == "Ocean Waves");
        Assert.True(video.Playable);
        Assert.Equal(Path.Combine(videoDir, "videos", "ocean.mp4"), video.MediaFile);
        Assert.Equal(Path.Combine(videoDir, "preview.jpg"), video.PreviewFile);

        var scene = items.Single(i => i.ContentType == "scene");
        Assert.False(scene.Playable);
        Assert.Equal(Path.Combine(sceneDir, "scene.pkg"), scene.MediaFile);

        // A project.json without a title falls back to the workshop folder name.
        Assert.Equal("333333333", items.Single(i => i.ProjectDir == untitledDir).Title);
    }

    [Fact]
    public void ListItems_SkipsItemsWithoutProjectJson_AndCorruptJson()
    {
        WriteItem("444444444", """{"contenttype":"video","file":"a.mp4","title":"Good"}""", ("a.mp4", new byte[] { 1 }));
        Directory.CreateDirectory(Path.Combine(_root, "555555555")); // no project.json
        var corruptDir = Path.Combine(_root, "666666666");
        Directory.CreateDirectory(corruptDir);
        File.WriteAllText(Path.Combine(corruptDir, "project.json"), "{ not json"); // corrupt

        var items = WallpaperEngineWorkshop.ListItems(_root);

        var item = Assert.Single(items);
        Assert.Equal("Good", item.Title);
    }

    [Fact]
    public void ListItems_NonexistentRoot_ReturnsEmpty()
    {
        Assert.Empty(WallpaperEngineWorkshop.ListItems(Path.Combine(_root, "does-not-exist")));
    }
}
