using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WallpaperProfiles.Infrastructure;

internal sealed record AppRelease(Version Version, Uri DownloadUrl, long Size, string Sha256);

internal sealed class AppUpdater
{
    internal const string Repository = "el-rato/Wallpaper-Profile-App";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };
    private readonly HttpClient _http;
    public static Version CurrentVersion => Normalize(typeof(AppUpdater).Assembly.GetName().Version!);
    public AppRelease? Available { get; private set; }
    public bool Busy { get; private set; }
    public string Status { get; private set; } = $"Installed version {CurrentVersion.ToString(3)}";
    public event Action? Changed;
    public event Action? UpdateFound;

    public AppUpdater(HttpClient? http = null) => _http = http ?? Http;

    internal static Version Normalize(Version version) => new(version.Major, version.Minor,
        Math.Max(0, version.Build), Math.Max(0, version.Revision));

    internal static AppRelease? ParseRelease(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+(\.\d+)?$") ||
            !Version.TryParse(tag.TrimStart('v'), out var version) || Normalize(version) <= Normalize(current)) return null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != "Belie.exe" ||
                asset.GetProperty("state").GetString() != "uploaded") continue;
            var url = asset.GetProperty("browser_download_url").GetString();
            var digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() : null;
            var size = asset.GetProperty("size").GetInt64();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                uri.Host != "github.com" || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort ||
                !uri.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.Ordinal) ||
                size <= 0 || size > 1024L * 1024 * 1024 ||
                digest == null || !Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$"))
                throw new InvalidDataException("The release does not contain a verified Belie update. Try again after the release finishes uploading.");
            return new AppRelease(Normalize(version), uri, size, digest[7..]);
        }
        throw new InvalidDataException("The latest release is missing Belie.exe. Try again after the release finishes uploading.");
    }

    public async Task CheckAsync()
    {
        if (Busy) return;
        Busy = true;
        SetStatus("Checking for updates…");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
            request.Headers.UserAgent.ParseAdd("Belie/" + CurrentVersion.ToString(3));
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            { SetStatus("No published releases found."); return; }
            response.EnsureSuccessStatusCode();
            var release = ParseRelease(await response.Content.ReadAsStringAsync(timeout.Token), CurrentVersion);
            var notify = release != null && release.Version != Available?.Version;
            Available = release;
            SetStatus(release == null ? "You’re up to date." : $"Version {release.Version.ToString(3)} is available.");
            if (notify) UpdateFound?.Invoke();
        }
        catch (Exception ex)
        {
            Logger.Error("Checking for updates failed.", ex);
            SetStatus(ex is InvalidDataException ? ex.Message : "Couldn’t check for updates. Check your connection and try again.");
        }
        finally { Busy = false; Changed?.Invoke(); }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000",
        Justification = "An empty Assembly.Location intentionally identifies the published single-file build.")]
    public async Task InstallAsync(Func<bool> canRestart, Action restart, CancellationToken cancellationToken)
    {
        if (Busy || Available == null) return;
        Busy = true;
        string? job = null;
        var handedOff = false;
        SetStatus("Preparing update…");
        try
        {
            // Only the published single-file application can safely replace itself.
            if (!string.IsNullOrEmpty(typeof(AppUpdater).Assembly.Location))
                throw new InvalidOperationException("In-app installation is available in the published Belie.exe build.");
            var target = Environment.ProcessPath ?? throw new IOException("The installed app could not be found.");
            job = Path.Combine(Path.GetDirectoryName(target)!, ".belie-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(job); // Fail before downloading if this installation is read-only.
            var release = Available;
            var payload = Path.Combine(job, "download.exe");
            var progress = new Progress<int>(percent => SetStatus($"Downloading update… {percent}%"));
            await DownloadAsync(release, payload, progress, cancellationToken);
            var fileVersion = FileVersionInfo.GetVersionInfo(payload).FileVersion;
            if (!Version.TryParse(fileVersion, out var version) || Normalize(version) != release.Version)
                throw new InvalidDataException("The update’s version does not match its release. The existing app has been kept.");
            cancellationToken.ThrowIfCancellationRequested();
            if (!canRestart()) { SetStatus("Update postponed. Your current version is still running."); return; }
            SetStatus("Restarting to install…");
            var helper = Path.Combine(job, "installer.exe");
            File.Copy(target, helper);
            using var parent = Process.GetCurrentProcess();
            var manifest = new UpdateJob(target, parent.Id, parent.StartTime.ToUniversalTime().Ticks, release.Size, release.Sha256);
            await File.WriteAllTextAsync(Path.Combine(job, "job.json"), JsonSerializer.Serialize(manifest), cancellationToken);
            var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = job };
            start.ArgumentList.Add("--install-update");
            using var process = Process.Start(start) ?? throw new IOException("The update installer could not start.");
            for (var i = 0; i < 150 && !File.Exists(Path.Combine(job, "ready")); i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited) throw new IOException("The update installer could not start.");
                await Task.Delay(100, cancellationToken);
            }
            if (!File.Exists(Path.Combine(job, "ready"))) throw new IOException("The update installer did not respond. Please try again.");
            // The helper will never replace anything without this explicit handoff.
            File.WriteAllText(Path.Combine(job, "commit"), "");
            handedOff = true;
            restart();
        }
        catch (OperationCanceledException) { SetStatus("Update cancelled."); }
        catch (Exception ex)
        {
            Logger.Error("Installing update failed.", ex);
            SetStatus(ex is UnauthorizedAccessException
                ? "This folder is read-only. Move Belie to a writable folder and try again."
                : ex is InvalidDataException or InvalidOperationException ? ex.Message
                : "Couldn’t install the update. Your current app is unchanged. Please try again.");
        }
        finally
        {
            if (!handedOff && job != null) CleanupJob(job);
            Busy = false;
            Changed?.Invoke();
        }
    }

    internal async Task DownloadAsync(AppRelease release, string path, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            using var request = new HttpRequestMessage(HttpMethod.Get, release.DownloadUrl);
            request.Headers.UserAgent.ParseAdd("Belie/" + CurrentVersion.ToString(3));
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long total = 0;
                var lastPercent = -1;
                int count;
                while ((count = await source.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    total += count;
                    if (total > release.Size) throw new InvalidDataException("The update download has an unexpected size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                    hash.AppendData(buffer, 0, count);
                    var percent = (int)(100 * total / release.Size);
                    if (percent != lastPercent) { progress?.Report(percent); lastPercent = percent; }
                }
                if (total != release.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The update download could not be verified. Please try again.");
            }
        }
        catch { try { File.Delete(path); } catch { } throw; }
    }

    private void SetStatus(string status) { Status = status; Changed?.Invoke(); }

    internal sealed record UpdateJob(string Target, int ParentId, long ParentStarted, long Size, string Sha256);

    // Runs from a separate copy of the published executable, before normal app startup.
    internal static void RunInstaller()
    {
        var job = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var payload = Path.Combine(job, "download.exe");
        var backup = Path.Combine(job, "previous.exe");
        UpdateJob? manifest = null;
        var replaced = false;
        var parentExited = false;
        try
        {
            manifest = JsonSerializer.Deserialize<UpdateJob>(File.ReadAllText(Path.Combine(job, "job.json")))
                ?? throw new InvalidDataException("Missing update instructions.");
            using var parent = Process.GetProcessById(manifest.ParentId);
            if (parent.StartTime.ToUniversalTime().Ticks != manifest.ParentStarted)
                throw new InvalidOperationException("The original app is no longer running.");
            using (var file = File.OpenRead(payload))
            {
                if (file.Length != manifest.Size || !Convert.ToHexString(SHA256.HashData(file)).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The update could not be verified.");
            }
            File.WriteAllText(Path.Combine(job, "ready"), "");
            for (var i = 0; i < 300 && !File.Exists(Path.Combine(job, "commit")); i++) Thread.Sleep(100);
            if (!File.Exists(Path.Combine(job, "commit"))) return;
            if (!parent.WaitForExit(60000)) throw new IOException("Belie is still running. Please try again.");
            parentExited = true;
            // Widget and video hosts may take a moment to release the original executable.
            for (var i = 0; ; i++)
            {
                try { File.Replace(payload, manifest.Target, backup); replaced = true; break; }
                catch (IOException) when (i < 120) { Thread.Sleep(250); }
            }
            StartApp(manifest.Target, job);
        }
        catch (Exception ex)
        {
            try
            {
                if (replaced && manifest != null) File.Copy(backup, manifest.Target, true);
                if (parentExited && manifest != null) StartApp(manifest.Target, job);
            }
            catch { /* Keep previous.exe for recovery if Windows prevents rollback. */ }
            System.Windows.MessageBox.Show("Belie could not finish updating. " + ex.Message +
                (File.Exists(backup) ? "\nYour previous build is saved at: " + backup : ""), "Belie update");
        }
    }

    private static void StartApp(string target, string job)
    {
        var start = new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)! };
        start.ArgumentList.Add("--updated-from");
        start.ArgumentList.Add(job);
        using var process = Process.Start(start) ?? throw new IOException("Belie could not restart.");
    }

    internal static async Task CleanupAfterRestartAsync(string job)
    {
        var target = Environment.ProcessPath!;
        var fullPath = Path.GetFullPath(job);
        if (!string.Equals(Path.GetDirectoryName(fullPath), Path.GetDirectoryName(target), StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(Path.GetFileName(fullPath), "^\\.belie-update-[a-f0-9]{32}$")) return;
        await Task.Delay(5000);
        CleanupJob(fullPath);
    }

    internal static void CleanupJob(string job)
    {
        // Delete only our own known files; never recursively remove an installation folder.
        foreach (var name in new[] { "download.exe", "installer.exe", "job.json", "ready", "commit", "previous.exe" })
            try { File.Delete(Path.Combine(job, name)); } catch { }
        try { Directory.Delete(job); } catch { }
    }
}
