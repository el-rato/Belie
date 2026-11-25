using System.Windows.Threading;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Engine;

internal sealed class SlideshowController : IDisposable
{
    private readonly DispatcherTimer _timer;
    private WallpaperProfile? _profile;
    private List<string> _images = new();
    private int _index;
    private readonly Queue<string> _randomQueue = new();
    private string _lastApplied = "";
    private string? _startAt;
    private int _busy, _wanted;
    /// <summary>What was shown before, so that going back works in a shuffled slideshow too.</summary>
    private readonly List<string> _history = new();
    private readonly Action<string, WallpaperProfile> _applyMedia;

    public SlideshowController(Action<string, WallpaperProfile>? applyMedia = null)
    {
        _applyMedia = applyMedia ?? ((path, profile) =>
            _ = WallpaperEngine.SetWallpaperAsync(path, profile.FitMode, WallpaperEngine.ApplyGeneration));
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (_, _) => _ = AdvanceAndApplyAsync();
    }

    /// <param name="startAt">The wallpaper to begin with, when the profile still has it.</param>
    public async void Start(WallpaperProfile profile, string? startAt = null)
    {
        Stop();
        _profile = profile;
        _startAt = startAt;
        // Folder enumeration (potentially a network share) happens off the UI thread.
        var fresh = await Task.Run(() => WallpaperEngine.GetProfileMedia(profile));
        if (!ReferenceEquals(_profile, profile))
        {
            return;
        }
        _images = fresh;
        _index = Math.Max(0, Find(_startAt));
        _startAt = null;
        _randomQueue.Clear();
        if (_images.Count > 0)
        {
            ApplyCurrent();
        }
        if (ReferenceEquals(_profile, profile)) RestartTimer();
    }

    private int Find(string? path) => path == null ? -1 : _images.FindIndex(i => string.Equals(i, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>A wallpaper that was just shown stays for a whole interval.</summary>
    private void RestartTimer()
    {
        _timer.Stop();
        if (_profile is not { SlideshowIntervalMinutes: > 0 } profile || _images.Count < 2) return;
        _timer.Interval = TimeSpan.FromMinutes(profile.SlideshowIntervalMinutes);
        _timer.Start();
    }

    /// <summary>Shows one of the wallpapers now. The slideshow carries on from there.</summary>
    public async Task ShowAsync(string path)
    {
        var profile = _profile;
        if (profile == null) return;
        if (Find(path) < 0)
        {
            // It may have been added since the folder was last read, or the folder is still being read.
            _startAt = path;
            var fresh = await Task.Run(() => WallpaperEngine.GetProfileMedia(profile));
            if (!ReferenceEquals(_profile, profile)) return;
            MergeScan(fresh);
            if (Find(path) < 0) return;
        }
        Remember();
        _index = Find(path);
        var later = _randomQueue.Where(item => !string.Equals(item, path, StringComparison.OrdinalIgnoreCase)).ToArray();
        _randomQueue.Clear();
        foreach (var item in later) _randomQueue.Enqueue(item);
        ApplyCurrent();
        RestartTimer();
    }

    /// <summary>The next wallpaper, or the one before. Presses that come quickly are added up.</summary>
    public async Task StepAsync(int by)
    {
        _wanted += by;
        if (_timer.IsEnabled) RestartTimer();
        await AdvanceAndApplyAsync(); // Does nothing while an earlier press is being handled, which picks this one up.
    }

    private void Remember()
    {
        if (_lastApplied.Length == 0) return;
        _history.Add(_lastApplied);
        if (_history.Count > 50) _history.RemoveAt(0);
    }

    public void Stop()
    {
        _timer.Stop();
        _profile = null;
        _images = new List<string>();
        _index = 0; _wanted = 0;
        _startAt = null;
        _history.Clear();
        _randomQueue.Clear();
    }

    private async Task AdvanceAndApplyAsync()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return;
        }
        try
        {
            // The timer asks for the next one.
            if (_wanted == 0) _wanted = 1;
            while (_wanted != 0)
            {
                var profile = _profile;
                if (profile == null)
                {
                    _wanted = 0;
                    return;
                }
                var fresh = await Task.Run(() => WallpaperEngine.GetProfileMedia(profile));
                if (!ReferenceEquals(_profile, profile))
                {
                    continue; // Another profile took over, and has its own presses.
                }
                MergeScan(fresh);
                var by = _wanted;
                _wanted = 0;
                if (_images.Count == 0)
                {
                    return;
                }
                if (_images.Count > 1) Move(by, profile.SlideshowRandom);
                ApplyCurrent();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private void Move(int by, bool random)
    {
        if (!random)
        {
            _index = ((_index + by) % _images.Count + _images.Count) % _images.Count;
            return;
        }
        for (; by > 0; by--) { Remember(); PickRandom(); _lastApplied = _images[_index]; }
        for (; by < 0; by++)
        {
            var found = -1;
            while (found < 0 && _history.Count > 0)
            {
                found = Find(_history[^1]);
                _history.RemoveAt(_history.Count - 1);
            }
            _index = found >= 0 ? found : (_index + _images.Count - 1) % _images.Count;
        }
    }

    private void MergeScan(List<string> fresh)
    {
        if (fresh.SequenceEqual(_images, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }
        var current = _index < _images.Count ? _images[_index] : null;
        _images = fresh;
        _index = current != null
            ? Math.Max(0, _images.FindIndex(i => string.Equals(i, current, StringComparison.OrdinalIgnoreCase)))
            : 0;
        _randomQueue.Clear();
    }

    private void PickRandom()
    {
        if (_randomQueue.Count == 0)
        {
            RefillRandomQueue();
        }
        while (_randomQueue.Count > 0)
        {
            var candidate = _randomQueue.Dequeue();
            var idx = _images.FindIndex(i => string.Equals(i, candidate, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _index = idx;
                return;
            }
        }
        _index = (_index + 1) % _images.Count;
    }

    private void RefillRandomQueue()
    {
        var candidates = _images.Where(i => !string.Equals(i, _lastApplied, StringComparison.OrdinalIgnoreCase)).ToList();
        if (candidates.Count == 0)
        {
            candidates = new List<string>(_images);
        }
        for (var i = candidates.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }
        foreach (var candidate in candidates)
        {
            _randomQueue.Enqueue(candidate);
        }
    }

    private void ApplyCurrent()
    {
        var profile = _profile;
        if (profile == null || _images.Count == 0)
        {
            return;
        }
        _lastApplied = _images[Math.Min(_index, _images.Count - 1)];
        _applyMedia(_lastApplied, profile);
    }

    public void Dispose() => Stop();
}
