using System.Globalization;
using System.Windows;
using System.Windows.Media;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using Color = System.Windows.Media.Color;

namespace WallpaperProfiles.Engine;

internal sealed class SceneController : IDisposable
{
    private static readonly string[] AccentKeys = { "SceneAccentColor", "SceneAccentHoverColor", "SceneAccentPressedColor", "SceneAccentSubtleColor" };
    private static readonly string[] AccentBrushKeys = { "AccentBrush", "AccentHoverBrush", "AccentPressedBrush", "AccentSubtleBrush" };
    private readonly ResourceDictionary? _resources;
    private Color[]? _originalColors;
    private object?[]? _originalBrushes;
    private MediaPlayer? _player;
    private string _audioName = "";
    private string _status = "No ambient track";
    private bool _ready;

    public event Action? StateChanged;
    public bool HasAudio => _player != null;
    public bool IsMuted { get; private set; }
    public string AudioStatus => _ready ? (IsMuted ? "Muted" : "Playing") + " · " + _audioName : _status;

    public SceneController(ResourceDictionary? resources = null)
    {
        var root = resources ?? (System.Windows.Application.Current?.Dispatcher.CheckAccess() == true
            ? System.Windows.Application.Current.Resources : null);
        _resources = root == null ? null : FindAccentResources(root);
    }

    private static ResourceDictionary? FindAccentResources(ResourceDictionary resources)
    {
        if (resources.Keys.Cast<object>().Contains(AccentKeys[0])) return resources;
        foreach (var merged in resources.MergedDictionaries.Reverse())
        {
            var found = FindAccentResources(merged);
            if (found != null) return found;
        }
        return null;
    }

    public static bool TryParseAccent(string? value, out Color color)
    {
        color = default;
        var text = value?.Trim() ?? "";
        if (text.Length != 7 || text[0] != '#') return false;
        if (!byte.TryParse(text.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red)
            || !byte.TryParse(text.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green)
            || !byte.TryParse(text.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue)) return false;
        color = Color.FromRgb(red, green, blue);
        return true;
    }

    public void Apply(WallpaperProfile profile)
    {
        StopAudio();
        ApplyAccent(profile.SceneAccent);
        IsMuted = profile.AmbientMuted;
        if (string.IsNullOrWhiteSpace(profile.AmbientAudioPath))
        {
            StateChanged?.Invoke();
            return;
        }
        if (!File.Exists(profile.AmbientAudioPath))
        {
            _status = "Ambient track missing — choose another file";
            StateChanged?.Invoke();
            return;
        }
        try
        {
            var player = new MediaPlayer { Volume = Math.Clamp(profile.AmbientVolume, 0, 100) / 100d, IsMuted = IsMuted };
            _player = player;
            _audioName = Path.GetFileNameWithoutExtension(profile.AmbientAudioPath);
            _status = "Loading · " + _audioName;
            player.MediaOpened += (_, _) =>
            {
                if (!ReferenceEquals(_player, player)) return;
                _ready = true;
                StateChanged?.Invoke();
            };
            player.MediaEnded += (_, _) =>
            {
                if (!ReferenceEquals(_player, player)) return;
                player.Position = TimeSpan.Zero;
                player.Play();
            };
            player.MediaFailed += (_, e) =>
            {
                if (!ReferenceEquals(_player, player)) return;
                Logger.Error("The scene's ambient track could not play.", e.ErrorException);
                StopAudio();
                _status = "Ambient audio unavailable — check this track";
                StateChanged?.Invoke();
            };
            player.Open(new Uri(Path.GetFullPath(profile.AmbientAudioPath)));
            player.Play();
        }
        catch (Exception ex)
        {
            Logger.Error("Starting ambient audio failed.", ex);
            StopAudio();
            _status = "Ambient audio unavailable — check this track";
        }
        StateChanged?.Invoke();
    }

    public void ToggleMuted()
    {
        if (_player == null) return;
        IsMuted = !IsMuted;
        _player.IsMuted = IsMuted;
        StateChanged?.Invoke();
    }

    private void ApplyAccent(string accent)
    {
        if (_resources == null || !AccentKeys.All(key => _resources[key] is Color)) return;
        if (!TryParseAccent(accent, out var color))
        {
            RestoreAccent();
            return;
        }
        _originalColors ??= AccentKeys.Select(key => (Color)_resources[key]).ToArray();
        _originalBrushes ??= AccentBrushKeys.Select(key => _resources.Contains(key) ? _resources[key] : null).ToArray();
        SetAccentColor(0, color);
        SetAccentColor(1, Mix(color, Colors.White, 0.2));
        SetAccentColor(2, Mix(color, Colors.Black, 0.15));
        SetAccentColor(3, Mix(color, Color.FromRgb(23, 25, 24), 0.82));
    }

    private void SetAccentColor(int index, Color color)
    {
        _resources![AccentKeys[index]] = color;
        _resources[AccentBrushKeys[index]] = new SolidColorBrush(color);
    }

    private static Color Mix(Color color, Color target, double amount) => Color.FromRgb(
        (byte)(color.R * (1 - amount) + target.R * amount),
        (byte)(color.G * (1 - amount) + target.G * amount),
        (byte)(color.B * (1 - amount) + target.B * amount));

    private void RestoreAccent()
    {
        if (_resources == null || _originalColors == null) return;
        for (var i = 0; i < AccentKeys.Length; i++)
        {
            _resources[AccentKeys[i]] = _originalColors[i];
            if (_originalBrushes?[i] is { } brush) _resources[AccentBrushKeys[i]] = brush;
            else _resources.Remove(AccentBrushKeys[i]);
        }
        _originalColors = null;
        _originalBrushes = null;
    }

    private void StopAudio()
    {
        var player = _player;
        _player = null;
        player?.Close();
        _ready = false;
        _status = "No ambient track";
    }

    public void Dispose()
    {
        StopAudio();
        RestoreAccent();
    }
}
