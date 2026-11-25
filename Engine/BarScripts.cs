using System.Diagnostics;
using System.Text;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Engine;

/// <summary>
/// Runs the commands of the bar's script modules now and then. The first line they write is shown on the bar, and
/// the rest when pointing at it.
/// </summary>
internal sealed class BarScripts : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private readonly Dictionary<BarModule, DateTime> _due = new();
    private readonly HashSet<BarModule> _running = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<DateTime> _now;
    private readonly Func<string, CancellationToken, Task<string>> _run;

    /// <summary>On the thread that asked.</summary>
    public event Action<BarModule, string, string>? Read;

    public BarScripts(Func<DateTime>? now = null, Func<string, CancellationToken, Task<string>>? run = null)
    {
        _now = now ?? (() => DateTime.UtcNow);
        _run = run ?? RunAsync;
    }

    /// <summary>Runs whatever is due. Called every second.</summary>
    public void Tick(IEnumerable<BarModule> modules)
    {
        // A script shows the output of a shell command; a "belie:" action produces none and must never be shelled.
        var scripts = modules.Where(module => module.Kind == BarModuleKind.Script && module.Command.Length > 0
            && !BarActions.ClaimsBuiltIn(module.Command)).ToArray();
        foreach (var gone in _due.Keys.Except(scripts).ToArray()) _due.Remove(gone);
        foreach (var script in scripts)
        {
            // One that takes longer than its interval is not started twice.
            if (_due.TryGetValue(script, out var due) && due > _now() || !_running.Add(script)) continue;
            _due[script] = _now().AddSeconds(script.Interval);
            Run(script);
        }
    }

    private async void Run(BarModule script)
    {
        try
        {
            var lines = (await _run(script.Command, _stop.Token)).Replace("\r", "").Trim('\n').Split('\n');
            if (!_stop.IsCancellationRequested) Read?.Invoke(script, Short(lines[0].Trim(), 200), Short(string.Join('\n', lines.Skip(1)).Trim(), 2000));
        }
        catch (OperationCanceledException) { if (!_stop.IsCancellationRequested) Read?.Invoke(script, "timed out", script.Command); }
        catch (Exception ex)
        {
            Logger.Warn("A bar script failed: " + ex.Message);
            if (!_stop.IsCancellationRequested) Read?.Invoke(script, "failed", ex.Message);
        }
        finally { _running.Remove(script); }
    }

    private static string Short(string text, int length) => text.Length <= length ? text : text[..length];

    /// <summary>PowerShell scripts run as they are; everything else goes to the command prompt.</summary>
    internal static ProcessStartInfo StartInfo(string command)
    {
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        var info = new ProcessStartInfo
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (command.Trim('"').EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            info.FileName = "powershell.exe";
            foreach (var argument in new[] { "-NoProfile", "-File", command.Trim('"') }) info.ArgumentList.Add(argument);
        }
        else
        {
            info.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            info.Arguments = "/d /s /c \"" + command + "\"";
        }
        return info;
    }

    private static Task<string> RunAsync(string command, CancellationToken stop) => Task.Run(async () =>
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
        limit.CancelAfter(Patience);
        using var process = Process.Start(StartInfo(command)) ?? throw new InvalidOperationException("It could not be started.");
        try
        {
            var error = process.StandardError.ReadToEndAsync(limit.Token);
            var output = await process.StandardOutput.ReadToEndAsync(limit.Token);
            await process.WaitForExitAsync(limit.Token);
            if (process.ExitCode != 0 && output.Length == 0) throw new InvalidOperationException(Short((await error).Trim(), 300));
            return output;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
    }, CancellationToken.None);

    public void Dispose() => _stop.Cancel();
}
