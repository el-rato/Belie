using System.Drawing;
using WallpaperProfiles.UI;
using Xunit;

namespace WallpaperProfiles.Tests;

/// <summary>
/// Windows tells docked bars "full screen" when the desktop gets focus too, so the bar only steps aside for a real
/// app that covers its monitor.
/// </summary>
public sealed class TopBarFullscreenTests
{
    private static readonly Rectangle Screen = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("")]
    public void TheDesktopIsNeverAFullscreenApp(string className)
        => Assert.False(TopBarWindow.IsFullscreenApp(className, Screen, Screen));

    [Fact]
    public void AGameCoveringTheMonitorIs()
        => Assert.True(TopBarWindow.IsFullscreenApp("UnityWndClass", Screen, Screen));

    [Fact]
    public void ABorderlessWindowSpillingPastTheEdgesIs()
        => Assert.True(TopBarWindow.IsFullscreenApp("Chrome_WidgetWin_1", new Rectangle(-8, -8, 1936, 1096), Screen));

    [Fact]
    public void AMaximizedWindowUnderTheBarIsNot()
        => Assert.False(TopBarWindow.IsFullscreenApp("Notepad", new Rectangle(0, 40, 1920, 1000), Screen));

    [Fact]
    public void ItalicTextGetsRoomForItsLean()
    {
        Assert.Equal(0, NowPlayingView.ItalicRoom(false, 24).Right);
        Assert.True(NowPlayingView.ItalicRoom(true, 24).Right >= 6);
    }
}
