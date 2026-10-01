using System.Text.Json;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Persistence;

internal sealed class LibraryStore
{
    private readonly string _path;

    public LibraryStore(string path) => _path = path;

    public bool Exists => File.Exists(_path);

    public List<WallpaperAsset> Load()
    {
        if (!File.Exists(_path)) return new();
        // A failed read is surfaced to the caller. Never replace unreadable metadata with defaults.
        var assets = JsonSerializer.Deserialize<List<WallpaperAsset>>(File.ReadAllText(_path), JsonOptions.Shared)
            ?? throw new InvalidDataException("The wallpaper library is empty or invalid.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<WallpaperAsset>();
        foreach (var asset in assets)
        {
            if (asset == null || string.IsNullOrWhiteSpace(asset.FilePath)) continue;
            asset.FilePath = Path.GetFullPath(asset.FilePath);
            if (!seen.Add(asset.FilePath)) continue;
            asset.Name = string.IsNullOrWhiteSpace(asset.Name) ? Path.GetFileNameWithoutExtension(asset.FilePath) : asset.Name;
            asset.Collection = asset.Collection?.Trim() ?? "";
            asset.Tags = (asset.Tags ?? new()).Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            result.Add(asset);
        }
        return result;
    }

    public static List<WallpaperAsset> Import(IEnumerable<WallpaperAsset> existing, IEnumerable<string> sources)
    {
        var seen = new HashSet<string>(existing.Select(a => a.FilePath), StringComparer.OrdinalIgnoreCase);
        var added = new List<WallpaperAsset>();
        foreach (var source in sources)
        {
            var files = Directory.Exists(source) ? Directory.EnumerateFiles(source) : new[] { source };
            foreach (var file in files)
            {
                if (!File.Exists(file) || !(WallpaperEngine.IsImageFile(file) || WallpaperEngine.IsVideoFile(file))) continue;
                var path = Path.GetFullPath(file);
                if (seen.Add(path))
                {
                    added.Add(new WallpaperAsset { FilePath = path, Name = Path.GetFileNameWithoutExtension(path) });
                }
            }
        }
        return added;
    }

    public void Save(IEnumerable<WallpaperAsset> assets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temp = _path + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(assets, JsonOptions.Shared));
            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
