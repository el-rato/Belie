using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using WallpaperProfiles.Infrastructure;

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

    /// <summary>How long a save or read waits for another Belie thread or process before going ahead anyway.</summary>
    internal static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(2);

    internal static string LockName(string path) => @"Local\Belie.Json." + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));

    internal static T WithLock<T>(string path, Func<T> action)
    {
        // Coordinate readers and publishers across store instances and processes. Windows
        // can reject a replacement while another thread still has the destination open.
        // The lock only smooths that contention: every save is still a complete file published
        // by an atomic rename (with retries), so when the lock cannot be had, because some other
        // process holds the name or created it with an ACL that shuts Belie out, go ahead
        // without it rather than letting that process block every save.
        Mutex mutex;
        try { mutex = LocalIpc.Mutex(false, LockName(path), out _); }
        catch (Exception error) when (error is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
        {
            Logger.Warn("The save lock for a settings file could not be opened; saving without it. " + error.GetType().Name);
            return action();
        }
        using (mutex)
        {
            var acquired = false;
            try
            {
                try { acquired = mutex.WaitOne(LockTimeout); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) Logger.Warn("The save lock for a settings file stayed busy; saving without it.");
                return action();
            }
            finally { if (acquired) mutex.ReleaseMutex(); }
        }
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
