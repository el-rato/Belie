using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;
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
