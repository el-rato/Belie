using System.IO;
using System.Drawing;
using WallpaperProfiles.Models;
using WallpaperProfiles.Workspaces;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed class PopoutTests
{
    private static readonly Rectangle Work = new(0, 40, 1920, 1000);

    [Theory]
    [InlineData("Picture in picture", false, 640, 360, true)]
    [InlineData("Picture-in-Picture", false, 1600, 900, true)]
    [InlineData("Meeting with the team", true, 480, 270, true)]
    [InlineData("Spotify", true, 300, 300, true)]
    [InlineData("Task Manager", true, 1600, 900, false)]
    [InlineData("Video player", false, 480, 270, false)]
    [InlineData("Picture in picture", false, 0, 0, false)]
    public void SmallAlwaysOnTopAndPictureInPictureWindowsArePopouts(string title, bool topmost, int width, int height, bool expected)
        => Assert.Equal(expected, Popouts.IsPopout(title, topmost, new Rectangle(100, 100, width, height), Work));

    [Fact]
    public void PopoutsStayOnEveryWorkspaceAndTheBarDrawsOverWindowsByDefault()
    {
        var settings = new TopBarSettings().Normalize();
        Assert.True(settings.StickyPopouts);
        Assert.True(settings.AutoHide);
    }

    [Fact]
    public void AppWorkspacesAreReadWrittenAndRepaired()
    {
        var apps = UI.BarSettingsPage.ParseAppWorkspaces("Firefox.exe: 2, spotify = 3, broken, discord: 99, : 4");
        var settings = new TopBarSettings { AppWorkspaces = apps }.Normalize();
        Assert.Equal(2, settings.AppWorkspaces["firefox"]);
        Assert.Equal(3, settings.AppWorkspaces["SPOTIFY"]);
        Assert.False(settings.AppWorkspaces.ContainsKey("discord"));
        Assert.Equal(2, settings.AppWorkspaces.Count);
        Assert.Equal("Firefox: 2, spotify: 3", UI.BarSettingsPage.FormatAppWorkspaces(settings.AppWorkspaces));
        Assert.True(new TopBarSettings().Normalize().RememberAppWorkspaces);
    }

    [Fact]
    public void SettingsSavedWithTilingStillLoad()
    {
        var file = Path.Combine(Path.GetTempPath(), $"belie-tiling-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(file, """{ "Enabled": true, "TilingEnabled": true, "TileGap": 12, "TileLayout": "Dwindle", "FloatingApps": ["calc"], "Height": 40 }""");
            var settings = new Persistence.TopBarStore(file).Load();
            Assert.True(settings.Enabled);
            Assert.Equal(40, settings.Height);
        }
        finally { File.Delete(file); }
    }
}
