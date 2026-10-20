using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace WallpaperProfiles.Persistence;

/// <summary>Shared JSON file IO. Each save publishes a complete file using its own temporary path.</summary>
internal static class AtomicJsonFile
{
    public static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    public static T? Read<T>(string path) => WithLock(path, () =>
    {
        using var stream = OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, JsonOptions.Shared);
    });

    public static void Write<T>(string path, T value) => WithLock(path, () =>
    {
        WriteCore(Path.GetFullPath(path), value);
        return true;
    });

    internal static T WithLock<T>(string path, Func<T> action)
    {
        path = Path.GetFullPath(path);
        // Coordinate readers and publishers across store instances and processes. Windows
        // can reject a replacement while another thread still has the destination open.
        var name = @"Local\Belie.Json." + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
        using var mutex = new Mutex(false, name);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("The file is busy. Try saving again.");
            return action();
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }

    private static void WriteCore<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, value, JsonOptions.Shared);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(temporary, path, overwrite: true);
                    break;
                }
                catch (Exception error) when (attempt < 4
                    && error is IOException or UnauthorizedAccessException
                    && (error.HResult & 0xffff) is 5 or 32 or 33)
                {
                    // Windows can briefly deny replacement even with delete sharing enabled.
                    // Retry publication only; permanent failures retain the previous file.
                    Thread.Sleep(20 << attempt);
                }
            }
        }
        finally
        {
            // Cleanup must not hide a serialization or replacement failure.
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
