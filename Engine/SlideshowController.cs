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
    private int _busy;
    private readonly Action<string, WallpaperProfile> _applyMedia;

    public SlideshowController(Action<string, WallpaperProfile>? applyMedia = null)
    {
        _applyMedia = applyMedia ?? ((path, profile) =>
            _ = WallpaperEngine.SetWallpaperAsync(path, profile.FitMode, WallpaperEngine.ApplyGeneration));
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (_, _) => _ = AdvanceAndApplyAsync();
    }

    public async void Start(WallpaperProfile profile)
    {
        Stop();
        _profile = profile;
        // Folder enumeration (potentially a network share) happens off the UI thread.
        var fresh = await Task.Run(() => WallpaperEngine.GetProfileMedia(profile));
        if (!ReferenceEquals(_profile, profile))
        {
            return;
        }
        _images = fresh;
        _index = 0;
        _randomQueue.Clear();
        if (_images.Count > 0)
        {
            ApplyCurrent();
        }
        var interval = Math.Max(0, profile.SlideshowIntervalMinutes);
        if (interval > 0 && _images.Count > 1 && ReferenceEquals(_profile, profile))
        {
            _timer.Interval = TimeSpan.FromMinutes(interval);
            _timer.Start();
        }
    }

    public void Stop()
    {
        _timer.Stop();
        _profile = null;
        _images = new List<string>();
        _index = 0;
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
            var profile = _profile;
            if (profile == null)
            {
                return;
            }
            var fresh = await Task.Run(() => WallpaperEngine.GetProfileMedia(profile));
            if (!ReferenceEquals(_profile, profile))
            {
                return;
            }
            MergeScan(fresh);
            if (_images.Count == 0)
            {
                return;
            }
            if (_images.Count > 1)
            {
                if (_profile.SlideshowRandom)
                {
                    PickRandom();
                }
                else
                {
                    _index = (_index + 1) % _images.Count;
                }
            }
            ApplyCurrent();
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
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
