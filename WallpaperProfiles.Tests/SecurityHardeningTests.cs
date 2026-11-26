using System.IO;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Threading;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Persistence;
using Xunit;

namespace WallpaperProfiles.Tests;

/// <summary>Regression tests for the medium/low findings of the 4.0.2 security audit.</summary>
public class SecurityHardeningTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("belie-hardening-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    private static string UniqueName(string kind) => @"Local\Belie.Test." + kind + "." + Guid.NewGuid().ToString("N");

    // Audit #4: named IPC objects carry an explicit owner-only DACL.
    [Fact]
    public void IpcEvent_AllowsOnlyTheCurrentUser()
    {
        using var handle = LocalIpc.Event(EventResetMode.AutoReset, UniqueName("Event"), out var createdNew);
        Assert.True(createdNew);
        var security = handle.GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
        var rule = Assert.Single(security.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier))
            .Cast<EventWaitHandleAccessRule>());
        Assert.Equal(LocalIpc.CurrentUser, rule.IdentityReference);
        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
    }

    [Fact]
    public void IpcMutex_AllowsOnlyTheCurrentUser()
    {
        using var mutex = LocalIpc.Mutex(false, UniqueName("Mutex"), out var createdNew);
        Assert.True(createdNew);
        var security = mutex.GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
        var rule = Assert.Single(security.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier))
            .Cast<MutexAccessRule>());
        Assert.Equal(LocalIpc.CurrentUser, rule.IdentityReference);
    }

    [Fact]
    public void IpcEvent_CanStillBeSignalledByTheSameUser()
    {
        var name = UniqueName("Signal");
        using var handle = LocalIpc.Event(EventResetMode.AutoReset, name);
        Assert.True(EventWaitHandle.TryOpenExisting(name, out var opened));
        using (opened) opened!.Set();
        Assert.True(handle.WaitOne(TimeSpan.FromSeconds(5)));
        // Opening an existing object reports it was not new.
        using var again = LocalIpc.Event(EventResetMode.AutoReset, name, out var createdNew);
        Assert.False(createdNew);
    }

    // Audit #6: the workshop path is passed as one escaped argument.
    [Theory]
    [InlineData(@"C:\Steam\steamapps\workshop\content\431960\123\project.json")]
    [InlineData(@"C:\Program Files (x86)\Steam\workshop\431960\1 2\project.json")]
    [InlineData("C:\\x\" -control stop -file \"C:\\y\\project.json")]
    public void WallpaperEngine_PathIsASingleArgument(string path)
    {
        var psi = WallpaperEngineWorkshop.OpenWallpaperStartInfo(@"C:\WE\wallpaper64.exe", path);
        Assert.False(psi.UseShellExecute);
        Assert.Equal("", psi.Arguments);
        Assert.Equal(new[] { "-control", "openWallpaper", "-file", path }, psi.ArgumentList);
    }

    [Theory]
    [InlineData("")]
    [InlineData("C:\\a\"b\\project.json")]
    [InlineData("C:\\a\nb\\project.json")]
    public void WallpaperEngine_RejectsUnusualPaths(string path) =>
        Assert.False(WallpaperEngineWorkshop.TryOpenViaWallpaperEngine(path));

    // Audit #7: another process holding or squatting the save lock cannot block saves.
    [Fact]
    public void AtomicSave_ProceedsWhenTheLockIsHeldElsewhere()
    {
        var path = Path.Combine(_directory, "held.json");
        using var holding = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            using var mutex = new Mutex(false, AtomicJsonFile.LockName(path));
            mutex.WaitOne();
            holding.Set();
            release.Wait();
            mutex.ReleaseMutex();
        }) { IsBackground = true };
        holder.Start();
        try
        {
            Assert.True(holding.Wait(TimeSpan.FromSeconds(10)));
            var clock = Stopwatch.StartNew();
            AtomicJsonFile.Write(path, new[] { 1, 2, 3 });
            Assert.Equal(new[] { 1, 2, 3 }, AtomicJsonFile.Read<int[]>(path));
            Assert.True(clock.Elapsed < AtomicJsonFile.LockTimeout * 2 + TimeSpan.FromSeconds(3), clock.Elapsed.ToString());
        }
        finally
        {
            release.Set();
            holder.Join();
        }
    }

    [Fact]
    public void AtomicSave_ProceedsWhenTheLockNameIsSquattedWithADenyingAcl()
    {
        var path = Path.Combine(_directory, "squatted.json");
        var security = new MutexSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false); // empty DACL: nobody may open it
        using var squatter = MutexAcl.Create(false, AtomicJsonFile.LockName(path), out var createdNew, security);
        Assert.True(createdNew);
        AtomicJsonFile.Write(path, new[] { 7 });
        Assert.Equal(new[] { 7 }, AtomicJsonFile.Read<int[]>(path));
    }

    [Fact]
    public void AtomicSave_LockNameDoesNotDependOnPathCaseOrForm()
    {
        var path = Path.Combine(_directory, "Case.json");
        Assert.Equal(AtomicJsonFile.LockName(path), AtomicJsonFile.LockName(path.ToUpperInvariant()));
        Assert.Equal(AtomicJsonFile.LockName(path),
            AtomicJsonFile.LockName(Path.Combine(_directory, "sub", "..", "case.json")));
    }
}
