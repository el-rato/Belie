using WallpaperProfiles.Persistence;
using WallpaperProfiles.UI;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed class LauncherSearchTests
{
    private static readonly LauncherCommand[] Commands =
    {
        new("Home", "Go to", "", () => { }, Keywords: "overview dashboard"),
        new("Library", "Go to", "", () => { }, Keywords: "explore images"),
        new("Apply Night drive", "Profile", "", () => { }, Keywords: "desktop set"),
        new("Edit Night drive", "Profile", "", () => { }),
        new("Pause automation", "Automation", "", () => { }, Keywords: "schedule"),
        new("Theme: Sage", "Look", "", () => { }, Keywords: "colour"),
    };

    private static string[] Titles(string query) => LauncherSearch.Filter(query, Commands).Select(command => command.Title).ToArray();

    [Fact]
    public void Empty_query_keeps_the_original_order()
    {
        Assert.Equal(Commands.Select(command => command.Title), Titles(""));
        Assert.Equal(Commands.Select(command => command.Title), Titles("   "));
    }

    [Fact]
    public void Title_prefix_beats_a_match_inside_a_word()
    {
        Assert.Equal("Library", Titles("lib")[0]);
        Assert.Equal("Home", Titles("ho")[0]);
    }

    [Fact]
    public void Every_word_has_to_match_somewhere()
    {
        Assert.Equal(new[] { "Apply Night drive" }, Titles("apply night"));
        Assert.Equal(new[] { "Edit Night drive", "Apply Night drive" }.OrderBy(x => x), Titles("night drive").OrderBy(x => x));
        Assert.Empty(Titles("apply zebra"));
    }

    [Fact]
    public void Keywords_and_groups_find_commands_too()
    {
        Assert.Equal("Home", Titles("dashboard")[0]);
        Assert.Equal("Pause automation", Titles("schedule")[0]);
        Assert.Contains("Theme: Sage", Titles("look"));
    }

    [Fact]
    public void Letters_in_order_still_match_as_a_last_resort()
    {
        Assert.Contains("Pause automation", Titles("pseauto"));
        Assert.Empty(Titles("q"));
    }

    [Fact]
    public void Launcher_keys_default_and_clean_up()
    {
        Assert.Equal(new[] { "Ctrl+Space", "Ctrl+K" }, new ShellSettings().LauncherKeys);
        Assert.Equal(new[] { "Ctrl+Space", "Ctrl+K" }, new ShellSettings { LauncherKeys = null! }.Normalize().LauncherKeys);
        Assert.Equal(new[] { "Ctrl+J" }, new ShellSettings { LauncherKeys = new() { " Ctrl+J ", "", "Ctrl+J" } }.Normalize().LauncherKeys);
    }
}
