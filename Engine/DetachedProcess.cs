using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace WallpaperProfiles.Engine;

/// <summary>Starts another copy of Belie that outlives the tray app.</summary>
internal static class DetachedProcess
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);

    public static void Start(params string[] arguments)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Belie's executable was not found.");
        using var currentProcess = Process.GetCurrentProcess();
        if (!IsProcessInJob(currentProcess.Handle, IntPtr.Zero, out var inJob))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (inJob)
        {
            // The WMI process provider starts an independent process outside the app's lifetime job.
            using var processClass = new ManagementClass("Win32_Process");
            using var parameters = processClass.GetMethodParameters("Create");
            parameters["CommandLine"] = $"\"{exe}\" " + string.Join(" ", arguments.Select(argument =>
                argument.StartsWith("--", StringComparison.Ordinal) ? argument : $"\"{argument}\""));
            parameters["CurrentDirectory"] = Path.GetDirectoryName(exe);
            using var result = processClass.InvokeMethod("Create", parameters, null);
            if (result == null || Convert.ToUInt32(result["ReturnValue"]) != 0)
                throw new InvalidOperationException("The independent Belie process could not start.");
            return;
        }
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("The Belie process could not start.");
    }
}
