using System.Security.Cryptography;
using System.Text;

namespace WallpaperProfiles.Infrastructure;

/// <summary>
/// Keeps the person's Wallhaven API key encrypted for their Windows account (DPAPI), outside settings.json.
/// </summary>
internal sealed class WallhavenKeyStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Belie.Wallhaven.ApiKey");

    public static string DefaultFile => Path.Combine(AppPaths.BaseDir, "wallhaven-key.bin");

    public string FilePath { get; }

    public WallhavenKeyStore(string? path = null) => FilePath = path ?? DefaultFile;

    public string Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return "";
            var key = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser));
            return WallhavenClient.IsValidApiKey(key) ? key : "";
        }
        catch (Exception ex)
        {
            Logger.Warn("The saved Wallhaven API key could not be read: " + ex.GetType().Name);
            return "";
        }
    }

    public void Save(string key)
    {
        key = key.Trim();
        if (!WallhavenClient.IsValidApiKey(key)) throw new ArgumentException("That doesn't look like a Wallhaven API key.", nameof(key));
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        File.WriteAllBytes(temporary, ProtectedData.Protect(Encoding.UTF8.GetBytes(key), Entropy, DataProtectionScope.CurrentUser));
        File.Move(temporary, FilePath, overwrite: true);
    }

    public void Clear()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }
}
