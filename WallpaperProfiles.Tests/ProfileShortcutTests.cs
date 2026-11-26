using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed class ProfileShortcutTests
{
    internal sealed class Hotkeys : IProfileHotkeys
    {
        public readonly Dictionary<int, string> Registered = new();
        public readonly HashSet<string> Unavailable = new();
        public int RegisterCalls;
        public event Action<int>? Pressed;
        public bool Register(int id, KeyGesture gesture)
        {
            RegisterCalls++;
            var text = ProfileShortcutManager.Format(gesture);
            if (Unavailable.Contains(text) || Registered.ContainsValue(text)) return false;
            Registered.Add(id, text); return true;
        }
        public void Unregister(int id) => Registered.Remove(id);
        public void Press(string shortcut) => Pressed?.Invoke(Registered.Single(b => b.Value == shortcut).Key);
        public void Dispose() => Registered.Clear();
    }

    [Theory]
    [InlineData("Ctrl+Alt+1", true)]
    [InlineData("Ctrl+Shift+F2", true)]
    [InlineData("Alt+Q", true)]
    [InlineData("Q", false)]
    [InlineData("Shift+F2", false)]
    [InlineData("Ctrl+F12", false)]
    [InlineData("Windows+Ctrl+1", false)]
    [InlineData("garbage", false)]
    public void ValidatesShortcuts(string shortcut, bool expected) =>
        Assert.Equal(expected, ProfileShortcutManager.TryParse(shortcut, out _, out _));

    [Fact]
    public void PersistsSwitchesReassignsAndClearsWithoutAWindow()
    {
        var directory = Path.Combine(Path.GetTempPath(), "belie-hotkeys-" + Guid.NewGuid());
        try
        {
            var store = new ProfileStore(directory);
            var first = new WallpaperProfile { Name = "Focus" };
            var second = new WallpaperProfile { Name = "Evening" };
            store.Save(first); store.Save(second);
            IReadOnlyList<WallpaperProfile> profiles = store.LoadAll();
            var hotkeys = new Hotkeys();
            Guid? active = null;
            using var manager = new ProfileShortcutManager(() => profiles, store.Save,
                () => profiles = store.LoadAll(), id => active = id, hotkeys);
            Assert.True(manager.TryAssign(first.Id, "Ctrl+Alt+1", out _));
            Assert.Equal("Ctrl+Alt+1", store.LoadAll().Single(p => p.Id == first.Id).KeyboardShortcut);
            hotkeys.Press("Ctrl+Alt+1"); Assert.Equal(first.Id, active);
            Assert.False(manager.TryAssign(second.Id, "Ctrl+Alt+1", out var duplicate));
            Assert.Contains("Focus", duplicate);
            var count = hotkeys.RegisterCalls;
            manager.Reload(); manager.Reload(); Assert.Equal(count, hotkeys.RegisterCalls);
            hotkeys.Unavailable.Add("Ctrl+Alt+2");
            Assert.False(manager.TryAssign(first.Id, "Ctrl+Alt+2", out _));
            Assert.Single(hotkeys.Registered); Assert.Contains("Ctrl+Alt+1", hotkeys.Registered.Values);
            Assert.True(manager.TryAssign(first.Id, "Ctrl+Alt+3", out _));
            Assert.Single(hotkeys.Registered); Assert.Contains("Ctrl+Alt+3", hotkeys.Registered.Values);
            manager.IsCapturing = true; Assert.Empty(hotkeys.Registered);
            manager.Reload(); Assert.Empty(hotkeys.Registered);
            manager.IsCapturing = false; hotkeys.Press("Ctrl+Alt+3"); Assert.Equal(first.Id, active);
            Assert.True(manager.TryAssign(first.Id, "", out _)); Assert.Empty(hotkeys.Registered);
            Assert.Empty(store.LoadAll().Single(p => p.Id == first.Id).KeyboardShortcut);
            Assert.True(manager.TryAssign(second.Id, "Ctrl+Alt+1", out _));
            store.Delete(second.Id); profiles = store.LoadAll(); manager.Reload(); Assert.Empty(hotkeys.Registered);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void WorkspaceKeysAreLeftToTheBarWhileItRuns()
    {
        TopBarSettings? bar = null;
        var taken = new Dictionary<string, object>(); // Windows gives keys to whoever asked first.
        var profile = new WallpaperProfile { Name = "Focus", KeyboardShortcut = "Alt+2" };
        var other = new WallpaperProfile { Name = "Evening", KeyboardShortcut = "Ctrl+Alt+E" };
        Guid? active = null;
        var tray = new SharedHotkeys(taken);
        using var manager = new ProfileShortcutManager(() => new[] { profile, other }, _ => { }, () => { }, id => active = id, tray,
            keys => bar?.WorkspaceFor(keys));
        Assert.Equal(new[] { "Alt+2", "Ctrl+Alt+E" }, taken.Keys.Order());

        var opened = new List<string>();
        long now = 0;
        SharedHotkeys? hotkeys = null;
        var settings = new TopBarSettings { Enabled = true, WorkspacesEnabled = true, WorkspaceCount = 3 };
        using var keys = new WorkspaceKeys(() => hotkeys = new SharedHotkeys(taken), (index, gesture) => opened.Add($"{index} {ProfileShortcutManager.Format(gesture)}"),
            _ => { }, () => now);
        keys.Apply(settings);
        Assert.Same(tray, taken["Alt+2"]); Assert.Same(hotkeys, taken["Alt+1"]);
        Assert.DoesNotContain("Ctrl+Alt+2", taken.Keys);
        // The tray app sees the bar and steps aside, and the bar gets its keys.
        bar = settings; manager.Reload();
        Assert.DoesNotContain("Alt+2", taken.Keys); Assert.Same(tray, taken["Ctrl+Alt+E"]);
        Assert.Null(manager.GetError(profile.Id)); Assert.Contains("workspace 2", manager.GetNote(profile.Id));
        keys.Retry();
        hotkeys!.Press("Alt+2");
        Assert.Equal("2 Alt+2", Assert.Single(opened)); Assert.Null(active);
        Assert.Equal(profile.Id, manager.OwnerOf("Alt+2")); Assert.Null(manager.OwnerOf("Alt+3"));
        // Assigning keys of the bar does not fail because it holds them.
        Assert.True(manager.TryAssign(other.Id, "Alt+3", out _)); Assert.Contains("workspace 3", manager.GetNote(other.Id));
        Assert.Same(hotkeys, taken["Alt+3"]);
        Assert.False(manager.TryAssign(other.Id, "Alt+2", out _));
        // Without the bar they are ordinary profile shortcuts again.
        keys.Dispose(); bar = null; manager.Reload();
        tray.Press("Alt+2"); Assert.Equal(profile.Id, active); Assert.Null(manager.GetNote(profile.Id));
    }

    [Fact]
    public void AnotherAppsKeysAreReplacedUntilTheyAreFree()
    {
        var taken = new Dictionary<string, object> { ["Alt+1"] = "another app" };
        var opened = new List<int>();
        long now = 0;
        SharedHotkeys? hotkeys = null;
        using var keys = new WorkspaceKeys(() => hotkeys = new SharedHotkeys(taken), (index, _) => opened.Add(index), _ => { }, () => now);
        keys.Apply(new TopBarSettings { Enabled = true, WorkspacesEnabled = true, WorkspaceCount = 2 });
        keys.Retry();
        Assert.DoesNotContain("Ctrl+Alt+1", taken.Keys);
        now += WorkspaceKeys.Patience; keys.Retry();
        hotkeys!.Press("Ctrl+Alt+1"); Assert.Equal(1, Assert.Single(opened));
        taken.Remove("Alt+1"); keys.Retry();
        hotkeys.Press("Alt+1"); Assert.Equal(2, opened.Count);
        Assert.DoesNotContain("Ctrl+Alt+1", taken.Keys);
        keys.Apply(new TopBarSettings { Enabled = true, WorkspacesEnabled = true, Shortcuts = WorkspaceShortcutModifier.None });
        Assert.Empty(taken);
    }

    private sealed class SharedHotkeys(Dictionary<string, object> taken) : IProfileHotkeys
    {
        private readonly Dictionary<int, string> _own = new();
        public event Action<int>? Pressed;
        public bool Register(int id, KeyGesture gesture)
        {
            var text = ProfileShortcutManager.Format(gesture);
            if (!taken.TryAdd(text, this)) return false;
            _own.Add(id, text); return true;
        }
        public void Unregister(int id) { if (_own.Remove(id, out var text)) taken.Remove(text); }
        public void Press(string shortcut) => Pressed?.Invoke(_own.Single(item => item.Value == shortcut).Key);
        public void Dispose() { foreach (var id in _own.Keys.ToArray()) Unregister(id); }
    }

    [Fact]
    public void FailedSaveKeepsPreviousShortcut()
    {
        var profile = new WallpaperProfile { KeyboardShortcut = "Ctrl+Alt+1" };
        var hotkeys = new Hotkeys();
        using var manager = new ProfileShortcutManager(() => new[] { profile },
            _ => throw new IOException("Disk unavailable"), () => { }, _ => { }, hotkeys);
        Assert.False(manager.TryAssign(profile.Id, "Ctrl+Alt+2", out _));
        Assert.Equal("Ctrl+Alt+1", profile.KeyboardShortcut);
        Assert.Single(hotkeys.Registered); Assert.Contains("Ctrl+Alt+1", hotkeys.Registered.Values);
    }

    [Fact]
    public void NativeHotkeyWindowReceivesMessages()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var native = new NativeProfileHotkeys();
                var received = 0;
                native.Pressed += id => received = id;
                SendMessage(native.Handle, 0x0312, new IntPtr(17), IntPtr.Zero);
                Assert.Equal(17, received);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}
