using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Interop;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Infrastructure;

internal interface IProfileHotkeys : IDisposable
{
    event Action<int>? Pressed;
    bool Register(int id, KeyGesture gesture);
    void Unregister(int id);
}

internal sealed class NativeProfileHotkeys : IProfileHotkeys
{
    private readonly HwndSource _source = new(new HwndSourceParameters("Belie.ProfileShortcuts")
    { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
    internal IntPtr Handle => _source.Handle;
    public event Action<int>? Pressed;

    public NativeProfileHotkeys() => _source.AddHook(Receive);
    private IntPtr Receive(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312) { handled = true; Pressed?.Invoke(wParam.ToInt32()); }
        return IntPtr.Zero;
    }
    public bool Register(int id, KeyGesture gesture) => RegisterHotKey(_source.Handle, id,
        (uint)gesture.Modifiers | 0x4000, (uint)KeyInterop.VirtualKeyFromKey(gesture.Key));
    public void Unregister(int id) => UnregisterHotKey(_source.Handle, id);
    public void Dispose() { _source.RemoveHook(Receive); _source.Dispose(); }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}

internal sealed class ProfileShortcutManager : IDisposable
{
    private sealed record Binding(int Id, string Shortcut);
    private readonly Func<IReadOnlyList<WallpaperProfile>> _profiles;
    private readonly Action<WallpaperProfile> _save;
    private readonly Action _refresh;
    private readonly Action<Guid> _activate;
    private readonly IProfileHotkeys _hotkeys;
    private readonly Dictionary<Guid, Binding> _bindings = new();
    private readonly Dictionary<Guid, string> _errors = new();
    private int _nextId;
    private bool _isCapturing;
    public bool IsCapturing
    {
        get => _isCapturing;
        set
        {
            if (_isCapturing == value) return;
            _isCapturing = value;
            if (value)
            {
                foreach (var binding in _bindings.Values) _hotkeys.Unregister(binding.Id);
                _bindings.Clear();
            }
            else Reload();
        }
    }

    public ProfileShortcutManager(Func<IReadOnlyList<WallpaperProfile>> profiles, Action<WallpaperProfile> save,
        Action refresh, Action<Guid> activate, IProfileHotkeys? hotkeys = null)
    {
        _profiles = profiles; _save = save; _refresh = refresh; _activate = activate;
        _hotkeys = hotkeys ?? new NativeProfileHotkeys();
        _hotkeys.Pressed += Pressed;
        Reload();
    }
    public string? GetError(Guid id) => _errors.GetValueOrDefault(id);

    internal static bool TryParse(string text, out KeyGesture? gesture, out string error)
    {
        gesture = null; error = "Use Ctrl or Alt with a letter, number, or function key.";
        try
        {
            gesture = new KeyGestureConverter().ConvertFromInvariantString(text) as KeyGesture;
            if (gesture == null || (gesture.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0
                || (gesture.Modifiers & ModifierKeys.Windows) != 0
                || gesture.Key is Key.None or Key.F12 or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                    or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            { gesture = null; return false; }
            error = ""; return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or FormatException)
        { gesture = null; return false; }
    }
    internal static string Format(KeyGesture gesture) => new KeyGestureConverter().ConvertToInvariantString(gesture)!;
    private int NextId()
    {
        do { _nextId = _nextId % 0xBFFF + 1; } while (_bindings.Values.Any(b => b.Id == _nextId));
        return _nextId;
    }
    public void Reload()
    {
        if (IsCapturing) return;
        var profiles = _profiles();
        foreach (var (id, binding) in _bindings.ToArray())
            if (!profiles.Any(p => p.Id == id && p.KeyboardShortcut == binding.Shortcut))
            { _hotkeys.Unregister(binding.Id); _bindings.Remove(id); }
        _errors.Clear();
        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.KeyboardShortcut) || _bindings.ContainsKey(profile.Id)) continue;
            if (!TryParse(profile.KeyboardShortcut, out var gesture, out var error))
            { _errors[profile.Id] = error; continue; }
            var canonical = Format(gesture!);
            if (_bindings.Values.Any(b => b.Shortcut == canonical))
            { _errors[profile.Id] = "This shortcut is assigned to another profile."; continue; }
            var id = NextId();
            if (_hotkeys.Register(id, gesture!)) _bindings[profile.Id] = new Binding(id, profile.KeyboardShortcut);
            else _errors[profile.Id] = "Shortcut unavailable. Choose another key combination.";
        }
    }
    public bool TryAssign(Guid profileId, string text, out string error)
    {
        error = "";
        var profile = _profiles().FirstOrDefault(p => p.Id == profileId);
        if (profile == null) { error = "Select a profile first."; return false; }
        KeyGesture? gesture = null;
        var shortcut = "";
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (!TryParse(text, out gesture, out error)) return false;
            shortcut = Format(gesture!);
            var duplicate = _profiles().FirstOrDefault(p => p.Id != profileId
                && TryParse(p.KeyboardShortcut, out var other, out _) && Format(other!) == shortcut);
            if (duplicate != null) { error = $"Already assigned to {duplicate.Name}. Choose another shortcut."; return false; }
        }
        _bindings.TryGetValue(profileId, out var previous);
        if (previous?.Shortcut == shortcut) return true;
        var newId = gesture == null ? 0 : NextId();
        if (gesture != null && !_hotkeys.Register(newId, gesture))
        { error = "Another app is using this shortcut. Choose another combination."; return false; }
        var copy = JsonSerializer.Deserialize<WallpaperProfile>(JsonSerializer.Serialize(profile))!;
        copy.KeyboardShortcut = shortcut;
        try { _save(copy); }
        catch (Exception ex)
        {
            if (newId != 0) _hotkeys.Unregister(newId);
            Logger.Error("Saving profile shortcut failed.", ex);
            error = "Couldn't save the shortcut. Your previous shortcut is still active."; return false;
        }
        if (previous != null) _hotkeys.Unregister(previous.Id);
        _bindings.Remove(profileId);
        if (newId != 0) _bindings[profileId] = new Binding(newId, shortcut);
        profile.KeyboardShortcut = shortcut;
        _errors.Remove(profileId);
        _refresh();
        return true;
    }
    private void Pressed(int id)
    {
        if (IsCapturing) return;
        foreach (var (profileId, binding) in _bindings)
            if (binding.Id == id) { _activate(profileId); return; }
    }
    public void Dispose()
    {
        _hotkeys.Pressed -= Pressed;
        foreach (var binding in _bindings.Values) _hotkeys.Unregister(binding.Id);
        _bindings.Clear(); _hotkeys.Dispose();
    }
}
