using System.IO;
using WallpaperProfiles.UI;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed class UserThemesTests
{
    [Fact]
    public void Three_colours_make_a_whole_theme()
    {
        var theme = UserThemes.Parse("""
            // A comment, and a trailing comma, are fine.
            { "name": "Gruvbox", "background": "#1D2021", "text": "#EBDBB2", "accent": "#FABD2F", }
            """, "gruvbox.json");
        Assert.Equal("Gruvbox", theme.Name);
        Assert.Equal("#1D2021", theme.Background);
        Assert.Equal("#FABD2F", theme.Accent);
        Assert.False(theme.Light);
        Assert.Equal("gruvbox.json", theme.FileName);
        // Worked-out colours step from the background toward the text.
        foreach (var derived in new[] { theme.Surface, theme.Raised, theme.Hover, theme.Border, theme.StrongBorder, theme.Secondary, theme.Muted, theme.Subtle })
            Assert.Matches("^#[0-9A-F]{6}$", derived);
        Assert.NotEqual(theme.Background, theme.Surface);
        Assert.True(Brightness(theme.Surface) < Brightness(theme.Raised) && Brightness(theme.Raised) < Brightness(theme.Hover));
    }

    [Fact]
    public void A_file_can_set_any_colour_and_takes_its_name_from_the_file()
    {
        var theme = UserThemes.Parse("""{ "Background": "#FAFAFA", "TEXT": "#202020", "accent": "#3366CC", "surface": "#ffffff", "danger": "#CC0000" }""", "paper.json");
        Assert.Equal("paper", theme.Name);
        Assert.Equal("#FFFFFF", theme.Surface);
        Assert.Equal("#CC0000", theme.Danger);
        Assert.True(theme.Light);
        Assert.False(UserThemes.Parse("""{ "background": "#FAFAFA", "text": "#202020", "accent": "#3366CC", "light": false }""", "x.json").Light);
    }

    [Theory]
    [InlineData("""{ "text": "#FFFFFF", "accent": "#FF0000" }""", "\"background\" is missing")]
    [InlineData("""{ "background": "black", "text": "#FFFFFF", "accent": "#FF0000" }""", "\"background\" must look like")]
    [InlineData("""{ "background": "#000000", "text": "#FFFFFF", "accent": "#FF0000", "border": "#12" }""", "\"border\" must look like")]
    [InlineData("""{ "background": "#000000", """, "not valid JSON")]
    public void Broken_files_say_what_is_wrong(string json, string message)
        => Assert.Contains(message, Assert.Throws<FormatException>(() => UserThemes.Parse(json, "broken.json")).Message);

    [Fact]
    public void The_folder_skips_notes_and_reports_clashes()
    {
        var folder = Path.Combine(Path.GetTempPath(), "belie-themes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            const string colours = "\"background\": \"#101010\", \"text\": \"#F0F0F0\", \"accent\": \"#88C0D0\"";
            File.WriteAllText(Path.Combine(folder, "_example.json"), "{ \"name\": \"Example\", " + colours + " }");
            File.WriteAllText(Path.Combine(folder, "a.json"), "{ \"name\": \"Mine\", " + colours + " }");
            File.WriteAllText(Path.Combine(folder, "b.json"), "{ \"name\": \"mine\", " + colours + " }");
            File.WriteAllText(Path.Combine(folder, "c.json"), "{ \"name\": \"Nord\", " + colours + " }");
            File.WriteAllText(Path.Combine(folder, "d.json"), "{ }");
            var (themes, problems) = UserThemes.Read(folder, UiAppearance.BuiltIn.Select(theme => theme.Name).ToList());
            Assert.Equal("Mine", Assert.Single(themes).Name);
            Assert.Equal(3, problems.Count);
            Assert.Contains(problems, problem => problem.StartsWith("b.json") && problem.Contains("already uses"));
            Assert.Contains(problems, problem => problem.StartsWith("c.json") && problem.Contains("built-in"));
            Assert.Contains(problems, problem => problem.StartsWith("d.json") && problem.Contains("missing"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Only_daylight_is_light_among_the_built_in_themes()
        => Assert.Equal(new[] { "Daylight" }, UiAppearance.BuiltIn.Where(theme => theme.Light).Select(theme => theme.Name));

    private static int Brightness(string hex) => Convert.ToInt32(hex[1..3], 16) + Convert.ToInt32(hex[3..5], 16) + Convert.ToInt32(hex[5..7], 16);
}
