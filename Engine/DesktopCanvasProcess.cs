using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Persistence;

namespace WallpaperProfiles.Engine;

internal static class DesktopCanvasProcess
{
    public static string DefaultFile => Path.Combine(AppPaths.BaseDir, "desktop-canvas.json");
    public static string MutexName(string file) => @"Local\Belie.Canvas." + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(file).ToUpperInvariant())))[..16];
    public static bool IsRunning(string file)
    {
        if (!Mutex.TryOpenExisting(MutexName(file), out var mutex)) return false;
        mutex.Dispose();
        return true;
    }
    public static void Start(string file)
    {
        var document = new DesktopCanvasStore(file).Load();
        if (!document.Enabled || !document.Widgets.Any(w => w.Enabled) || IsRunning(file)) return;
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Belie's executable was not found.");
        var canvasFile = Path.GetFullPath(file);
        using var currentProcess = Process.GetCurrentProcess();
        if (!IsProcessInJob(currentProcess.Handle, IntPtr.Zero, out var inJob))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (inJob)
        {
            // The WMI process provider starts an independent process outside the app's lifetime job.
            using var processClass = new ManagementClass("Win32_Process");
            using var arguments = processClass.GetMethodParameters("Create");
            arguments["CommandLine"] = $"\"{exe}\" --widget-host --canvas-file \"{canvasFile}\"";
            arguments["CurrentDirectory"] = Path.GetDirectoryName(exe);
            using var result = processClass.InvokeMethod("Create", arguments, null);
            if (result == null || Convert.ToUInt32(result["ReturnValue"]) != 0)
                throw new InvalidOperationException("The independent desktop widget host could not start.");
            Logger.Info("Desktop widgets started independently of the tray app.");
            return;
        }
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--widget-host");
        info.ArgumentList.Add("--canvas-file");
        info.ArgumentList.Add(canvasFile);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("The desktop widgets could not start.");
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);
    public static void Stop(string file)
    {
        if (!EventWaitHandle.TryOpenExisting(MutexName(file) + ".Stop", out var stop)) return;
        using (stop) stop.Set();
    }
}
