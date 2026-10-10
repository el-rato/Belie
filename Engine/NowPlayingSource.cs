using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Media;
using WallpaperProfiles.Infrastructure;

namespace WallpaperProfiles.Engine;

internal enum PlaybackCommand { Toggle, Previous, Next, Seek }
internal sealed record MusicSessionChoice(string AppId, MediaPlaybackType? PlaybackType, MediaPlaybackType? MetadataType,
    bool Playing, bool Current, bool Previous = false, IReadOnlyList<string>? Genres = null);

internal sealed record NowPlayingState
{
    public string Title { get; init; } = "Nothing playing";
    public string Artist { get; init; } = "Play music in a supported music app.";
    public string Album { get; init; } = "";
    public string Player { get; init; } = "";
    public ImageSource? Artwork { get; init; }
    public bool HasSession { get; init; }
    public bool IsPlaying { get; init; }
    public bool CanToggle { get; init; }
    public bool CanPrevious { get; init; }
    public bool CanNext { get; init; }
    public bool CanSeek { get; init; }
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
    public TimeSpan Position { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public double Rate { get; init; } = 1;
    public string? Error { get; init; }
    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;
    public TimeSpan Elapsed(DateTimeOffset now)
    {
        var seconds = (Position - Start).TotalSeconds;
        if (IsPlaying) seconds += Math.Max(0, (now - UpdatedAt).TotalSeconds) * Rate;
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Duration.TotalSeconds));
    }
    public static string TimeLabel(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
        : $"{(int)value.TotalMinutes}:{value.Seconds:00}";
}

internal interface INowPlayingSource : IDisposable
{
    Task<NowPlayingState> ReadAsync(CancellationToken token);
    Task<bool> SendAsync(PlaybackCommand command, TimeSpan position, CancellationToken token);
}

internal sealed class WindowsNowPlayingSource : INowPlayingSource
{
    private static readonly HashSet<string> MusicPlayers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Spotify", "SpotifyAB.SpotifyMusic", "AppleMusic", "AppleInc.AppleMusic", "iTunes",
        "AmazonMusic", "AmazonMobileLLC.AmazonMusic", "TIDAL", "Deezer", "Deezer.62021768415AF",
        "Qobuz", "MusicBee", "foobar2000", "Winamp", "AIMP", "Microsoft.ZuneMusic",
        "Microsoft.WindowsMediaPlayer", "wmplayer", "VideoLAN.VLC", "vlc"
    };
    private static bool IsMusicPlayer(string appId) => MusicPlayers.Contains(PlayerName(appId));
    internal static bool IsMusic(MusicSessionChoice choice) => IsMusicPlayer(choice.AppId)
        && (choice.PlaybackType == MediaPlaybackType.Music || choice.MetadataType == MediaPlaybackType.Music)
        && (choice.PlaybackType == null || choice.PlaybackType == MediaPlaybackType.Music)
        && (choice.MetadataType == null || choice.MetadataType == MediaPlaybackType.Music)
        && !(choice.Genres?.Any(genre => new[] { "podcast", "audiobook", "spoken word", "speech" }
            .Any(kind => genre.Contains(kind, StringComparison.OrdinalIgnoreCase))) ?? false);
    internal static int? ChooseMusicSession(IReadOnlyList<MusicSessionChoice> sessions) => Enumerable.Range(0, sessions.Count)
        .Where(index => IsMusic(sessions[index])).OrderByDescending(index => sessions[index].Playing)
        .ThenByDescending(index => sessions[index].Current).ThenByDescending(index => sessions[index].Previous)
        .Select(index => (int?)index).FirstOrDefault();
    private static Task<GlobalSystemMediaTransportControlsSessionManager>? _managerTask;
    private static readonly object ManagerLock = new();
    private GlobalSystemMediaTransportControlsSession? _session;
    private int _mediaVersion;
    private int _readVersion = -1;
    private NowPlayingState _metadata = new();
    private bool _disposed;
    private string? _lastError;

    private static Task<GlobalSystemMediaTransportControlsSessionManager> Manager()
    {
        lock (ManagerLock)
        {
            if (_managerTask == null || _managerTask.IsFaulted || _managerTask.IsCanceled)
                _managerTask = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
            return _managerTask;
        }
    }

    private void MediaChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        => Interlocked.Increment(ref _mediaVersion);

    private void UseSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(_session, session)) return;
        if (_session != null) _session.MediaPropertiesChanged -= MediaChanged;
        _session = session;
        _readVersion = -1;
        Interlocked.Increment(ref _mediaVersion);
        _metadata = new();
        if (session != null) session.MediaPropertiesChanged += MediaChanged;
    }

    public async Task<NowPlayingState> ReadAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            var manager = await Manager().WaitAsync(token);
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            var current = manager.GetCurrentSession();
            var candidates = new List<(GlobalSystemMediaTransportControlsSession Session,
                GlobalSystemMediaTransportControlsSessionMediaProperties Media, MusicSessionChoice Choice)>();
            foreach (var candidate in manager.GetSessions())
            {
                token.ThrowIfCancellationRequested();
                if (!IsMusicPlayer(candidate.SourceAppUserModelId)) continue;
                try
                {
                    var info = candidate.GetPlaybackInfo();
                    var media = await candidate.TryGetMediaPropertiesAsync().AsTask(token);
                    candidates.Add((candidate, media, new MusicSessionChoice(candidate.SourceAppUserModelId,
                        info.PlaybackType, media.PlaybackType,
                        info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                        ReferenceEquals(candidate, current), ReferenceEquals(candidate, _session), media.Genres.ToArray())));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch { /* A closed or unreadable player must not hide another music session. */ }
            }
            var selected = ChooseMusicSession(candidates.Select(candidate => candidate.Choice).ToArray());
            var session = selected.HasValue ? candidates[selected.Value].Session : null;
            UseSession(session);
            if (session == null) return new();
            var version = Volatile.Read(ref _mediaVersion);
            if (_readVersion != version)
            {
                var media = candidates[selected!.Value].Media;
                var artwork = await ReadArtworkAsync(media, token);
                token.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                _metadata = new NowPlayingState
                {
                    Title = string.IsNullOrWhiteSpace(media.Title) ? "Unknown track" : media.Title,
                    Artist = string.IsNullOrWhiteSpace(media.Artist) ? media.AlbumArtist : media.Artist,
                    Album = media.AlbumTitle, Artwork = artwork,
                    Player = PlayerName(session.SourceAppUserModelId), HasSession = true
                };
                _readVersion = version;
            }
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var controls = playback.Controls;
            var playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            _lastError = null;
            return _metadata with
            {
                IsPlaying = playing,
                CanToggle = playing ? controls.IsPauseEnabled || controls.IsPlayPauseToggleEnabled : controls.IsPlayEnabled || controls.IsPlayPauseToggleEnabled,
                CanPrevious = controls.IsPreviousEnabled, CanNext = controls.IsNextEnabled,
                CanSeek = controls.IsPlaybackPositionEnabled,
                Start = timeline.StartTime, End = timeline.EndTime, Position = timeline.Position,
                UpdatedAt = timeline.LastUpdatedTime == default ? DateTimeOffset.UtcNow : timeline.LastUpdatedTime,
                Rate = playback.PlaybackRate is { } rate && double.IsFinite(rate) && rate >= 0 ? rate : 1
            };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (!_disposed)
        {
            UseSession(null);
            if (_lastError != ex.Message) Logger.Error("Reading Windows media playback failed.", ex);
            _lastError = ex.Message;
            return new NowPlayingState { Title = "Playback unavailable", Artist = "Try reopening your music player.", Error = "Windows could not share the current playback session." };
        }
    }

    private static async Task<ImageSource?> ReadArtworkAsync(GlobalSystemMediaTransportControlsSessionMediaProperties media, CancellationToken token)
    {
        if (media.Thumbnail == null) return null;
        try
        {
            using var randomAccess = await media.Thumbnail.OpenReadAsync().AsTask(token);
            if (randomAccess.Size > 4 * 1024 * 1024) return null;
            using var input = randomAccess.AsStreamForRead();
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int count;
            while ((count = await input.ReadAsync(buffer, token)) > 0)
            {
                if (output.Length + count > 4 * 1024 * 1024) return null;
                output.Write(buffer, 0, count);
            }
            var bytes = output.ToArray();
            return await Task.Run(() =>
            {
                using var stream = new MemoryStream(bytes);
                var image = new BitmapImage();
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 320; image.StreamSource = stream; image.EndInit(); image.Freeze();
                return (ImageSource)image;
            }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    public async Task<bool> SendAsync(PlaybackCommand command, TimeSpan position, CancellationToken token)
    {
        if (_disposed || _session is not { } session) return false;
        var info = session.GetPlaybackInfo();
        var media = await session.TryGetMediaPropertiesAsync().AsTask(token);
        if (!IsMusic(new MusicSessionChoice(session.SourceAppUserModelId, info.PlaybackType, media.PlaybackType,
            false, false, Genres: media.Genres.ToArray()))) { UseSession(null); return false; }
        var controls = info.Controls;
        return command switch
        {
            PlaybackCommand.Toggle when controls.IsPlayPauseToggleEnabled => await session.TryTogglePlayPauseAsync().AsTask(token),
            PlaybackCommand.Toggle when info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && controls.IsPauseEnabled => await session.TryPauseAsync().AsTask(token),
            PlaybackCommand.Toggle when info.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && controls.IsPlayEnabled => await session.TryPlayAsync().AsTask(token),
            PlaybackCommand.Previous when controls.IsPreviousEnabled => await session.TrySkipPreviousAsync().AsTask(token),
            PlaybackCommand.Next when controls.IsNextEnabled => await session.TrySkipNextAsync().AsTask(token),
            PlaybackCommand.Seek when controls.IsPlaybackPositionEnabled => await session.TryChangePlaybackPositionAsync(position.Ticks).AsTask(token),
            _ => false
        };
    }

    private static string PlayerName(string id)
    {
        var name = id.Split('!')[0];
        var package = name.IndexOf('_');
        if (package > 0) name = name[..package];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; UseSession(null);
    }
}
