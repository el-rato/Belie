namespace WallpaperProfiles.Infrastructure;

internal static class Logger
{
    private static readonly object Gate = new();
    private static string _dir = "";

    public static void Init(string dir)
    {
        _dir = dir;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.log"))
            {
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-14))
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
        }
        Info($"--- Session started ({Environment.OSVersion.VersionString}, {(Environment.Is64BitProcess ? "x64" : "x86")}) ---");
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex == null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(Path.Combine(_dir, $"{DateTime.Now:yyyy-MM-dd}.log"), line);
            }
        }
        catch
        {
        }
    }
}
