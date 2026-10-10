using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.UI;
using Windows.Media;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed class NowPlayingTests
{
    [Theory]
    [InlineData("Spotify.exe", MediaPlaybackType.Music, MediaPlaybackType.Music, true)]
    [InlineData("AppleInc.AppleMusic_win!App", null, MediaPlaybackType.Music, true)]
    [InlineData("TIDAL.exe", MediaPlaybackType.Music, null, true)]
    [InlineData("Spotify.exe", MediaPlaybackType.Video, MediaPlaybackType.Music, false)]
    [InlineData("vlc.exe", MediaPlaybackType.Video, MediaPlaybackType.Video, false)]
    [InlineData("wmplayer.exe", MediaPlaybackType.Image, MediaPlaybackType.Image, false)]
    [InlineData("Spotify.exe", null, null, false)]
    [InlineData("Spotify.exe", MediaPlaybackType.Unknown, MediaPlaybackType.Unknown, false)]
    [InlineData("chrome.exe", MediaPlaybackType.Music, MediaPlaybackType.Music, false)]
    [InlineData("MSEdge", MediaPlaybackType.Music, MediaPlaybackType.Music, false)]
    [InlineData("firefox.exe", MediaPlaybackType.Music, MediaPlaybackType.Music, false)]
    [InlineData("UnknownPlayer.exe", MediaPlaybackType.Music, MediaPlaybackType.Music, false)]
    public void MusicFilter_AcceptsMusicPlayersOnly(string app, MediaPlaybackType? playback, MediaPlaybackType? metadata, bool expected)
        => Assert.Equal(expected, WindowsNowPlayingSource.IsMusic(new MusicSessionChoice(app, playback, metadata, true, true)));

    [Fact]
    public void MusicSelection_IgnoresCurrentVideo_AndKeepsPlayingMusic()
    {
        var browser = new MusicSessionChoice("chrome.exe", MediaPlaybackType.Music, MediaPlaybackType.Music, true, true);
        var paused = new MusicSessionChoice("AppleMusic.exe", MediaPlaybackType.Music, MediaPlaybackType.Music, false, false);
        var song = new MusicSessionChoice("Spotify.exe", MediaPlaybackType.Music, MediaPlaybackType.Music, true, false);
        Assert.Equal(2, WindowsNowPlayingSource.ChooseMusicSession(new[] { browser, paused, song }));
        Assert.Equal(1, WindowsNowPlayingSource.ChooseMusicSession(new[] { browser, paused }));
        Assert.Null(WindowsNowPlayingSource.ChooseMusicSession(new[] { browser }));
        Assert.Null(WindowsNowPlayingSource.ChooseMusicSession(Array.Empty<MusicSessionChoice>()));
        Assert.False(WindowsNowPlayingSource.IsMusic(song with { Genres = new[] { "Podcasts" } }));
        Assert.False(WindowsNowPlayingSource.IsMusic(song with { Genres = new[] { "Audiobook" } }));
    }
    [Fact]
    public void Timeline_InterpolatesPlayingOnly_AndClampsToTrackBounds()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new NowPlayingState { IsPlaying = true, Start = TimeSpan.FromSeconds(10), End = TimeSpan.FromSeconds(190),
            Position = TimeSpan.FromSeconds(40), UpdatedAt = now, Rate = 2 };
        Assert.Equal(TimeSpan.FromSeconds(50), state.Elapsed(now.AddSeconds(10)));
        Assert.Equal(TimeSpan.FromSeconds(30), (state with { IsPlaying = false }).Elapsed(now.AddHours(1)));
        Assert.Equal(state.Duration, state.Elapsed(now.AddHours(1)));
        Assert.Equal(TimeSpan.Zero, (state with { Position = TimeSpan.Zero, IsPlaying = false }).Elapsed(now));
        Assert.Equal("1:02:03", NowPlayingState.TimeLabel(TimeSpan.FromSeconds(3723)));
    }

    [Fact]
    public async Task WindowsMediaSession_CanBeReadWithoutChangingPlayback()
    {
        using var source = new WindowsNowPlayingSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var state = await source.ReadAsync(timeout.Token);
        Assert.Null(state.Error);
    }

    [Fact]
    public void PlayerView_UpdatesTracksAndControls_AndStopsWhenUnloaded()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    var source = new FakeSource();
                    var widget = new DesktopWidget { Kind = DesktopWidgetKind.NowPlaying, ShowBorder = false, ShowBackground = false, ShowHeader = false };
                    var view = new NowPlayingView(widget, () => source);
                    view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                    await view.RefreshAsync();
                    Assert.Contains(Children(view).OfType<TextBlock>(), t => t.Text == "First song");
                    Assert.Contains(Children(view).OfType<Button>(), b => Equals(b.ToolTip, "Pause") && b.IsEnabled);
                    await view.SendAsync(PlaybackCommand.Toggle, TimeSpan.Zero);
                    Assert.Equal(PlaybackCommand.Toggle, Assert.Single(source.Commands).Command);
                    await view.SendAsync(PlaybackCommand.Seek, TimeSpan.FromHours(2));
                    Assert.Equal(TimeSpan.FromSeconds(180), source.Commands.Last().Position);
                    source.State = source.State with { Title = "Next song", IsPlaying = false, CanNext = false };
                    await view.RefreshAsync();
                    Assert.Contains(Children(view).OfType<TextBlock>(), t => t.Text == "Next song");
                    Assert.Contains(Children(view).OfType<Button>(), b => Equals(b.ToolTip, "Play") && b.IsEnabled);
                    var before = source.Commands.Count;
                    await view.SendAsync(PlaybackCommand.Next, TimeSpan.Zero);
                    Assert.Equal(before, source.Commands.Count);
                    source.Succeed = false;
                    await view.SendAsync(PlaybackCommand.Toggle, TimeSpan.Zero);
                    Assert.Contains(Children(view).OfType<TextBlock>(), t => t.Text.Contains("could not perform"));
                    source.State = new(); await view.RefreshAsync();
                    Assert.Contains(Children(view).OfType<TextBlock>(), t => t.Text == "Nothing playing");
                    Assert.All(Children(view).OfType<Button>(), b => Assert.False(b.IsEnabled));
                    view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                    Assert.True(source.Disposed);
                    var reads = source.Reads; await view.RefreshAsync(); Assert.Equal(reads, source.Reads);
                }
                catch (Exception ex) { failure = ex; }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        if (root is FrameworkElement element) { element.ApplyTemplate(); element.Measure(new Size(420, 190)); element.Arrange(new Rect(0, 0, 420, 190)); }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var next in Children(child)) yield return next;
        }
    }

    private sealed class FakeSource : INowPlayingSource
    {
        public NowPlayingState State = new() { Title = "First song", Artist = "Artist", HasSession = true, IsPlaying = true,
            CanToggle = true, CanSeek = true, CanNext = true, CanPrevious = true, End = TimeSpan.FromSeconds(180) };
        public List<(PlaybackCommand Command, TimeSpan Position)> Commands = new();
        public int Reads;
        public bool Disposed, Succeed = true;
        public Task<NowPlayingState> ReadAsync(CancellationToken token) { Reads++; return Task.FromResult(State); }
        public Task<bool> SendAsync(PlaybackCommand command, TimeSpan position, CancellationToken token) { Commands.Add((command, position)); return Task.FromResult(Succeed); }
        public void Dispose() => Disposed = true;
    }
}
