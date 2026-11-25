using WallpaperProfiles.Engine;
using WallpaperProfiles.Models;
using Xunit;

namespace WallpaperProfiles.Tests;

/// <summary>
/// A shared look must not be able to run code that the import review never showed. These lock down the one
/// allow-list every path agrees on: review, stripping, the launcher and the script runner.
/// </summary>
public sealed class BarActionSafetyTests
{
    [Theory]
    [InlineData("belie:play", true)]
    [InlineData("BELIE:PLAY", true)]
    [InlineData("  belie:mute  ", true)]
    [InlineData("belie:next-workspace", true)]
    [InlineData("belie:& calc", false)]      // the exploit: a chained shell command wearing a belie: prefix
    [InlineData("belie:wallpapersX", false)] // not an exact action
    [InlineData("belie:", false)]
    [InlineData("calc", false)]
    public void OnlyExactActionsCountAsBuiltIn(string command, bool expected)
        => Assert.Equal(expected, BarActions.IsBuiltIn(command));

    [Fact]
    public void Review_ReportsBelieChainedCommands_AndHidesOnlyExactActions()
    {
        // The chained payload is now surfaced to the import dialog instead of being filtered out.
        Assert.Contains("belie:& calc", new BarModule(BarModuleKind.Script) { Command = "belie:& calc" }.Commands);
        Assert.Contains("notepad", new BarModule(BarModuleKind.Launcher) { Command = "notepad" }.Commands);
        // A genuine built-in action stays hidden, so ordinary looks don't nag.
        Assert.Empty(new BarModule(BarModuleKind.Volume) { OnClick = "belie:mute" }.Commands);
    }

    [Fact]
    public void ForeignLook_WithHiddenScriptPayload_IsReviewableAndStrippable()
    {
        var look = new BarRice
        {
            Name = "Pretty",
            Look = new TopBarSettings
            {
                Left = new(), Center = new(), // keep Normalize() from adding default modules
                Right = new()
                {
                    new BarModule(BarModuleKind.Script) { Command = "belie:& calc" },
                    new BarModule(BarModuleKind.Volume) { OnClick = "belie:& calc", OnRightClick = "belie:mute" },
                },
            },
        };
        // ImportLook only prompts when Commands is non-empty, so the payload must appear here.
        Assert.Contains("belie:& calc", look.Commands);

        look.RemoveCommands();
        // The script module carrying it is removed; the harmless module stays but its payload is cleared.
        Assert.DoesNotContain(look.Look.Normalize().Modules, module => module.Kind == BarModuleKind.Script);
        var volume = Assert.Single(look.Look.Normalize().Modules);
        Assert.Equal("", volume.OnClick);
        Assert.Equal("belie:mute", volume.OnRightClick); // a recognized action is left alone
        Assert.Empty(look.Commands);
    }

    [Fact]
    public void ScriptRunner_SkipsBelieCommands_ButRunsRealOnes()
    {
        var ran = new List<string>();
        using var scripts = new BarScripts(run: (command, _) => { ran.Add(command); return Task.FromResult(""); });
        scripts.Tick(new[]
        {
            new BarModule(BarModuleKind.Script) { Command = "belie:& calc" }, // must never be shelled
            new BarModule(BarModuleKind.Script) { Command = "belie:play" },
            new BarModule(BarModuleKind.Script) { Command = "echo hi" },
        });
        Assert.Equal(new[] { "echo hi" }, ran);
    }
}
