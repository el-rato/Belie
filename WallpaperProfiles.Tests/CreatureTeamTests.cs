using System.IO;
using System.Drawing;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Models;
using Xunit;

namespace WallpaperProfiles.Tests;

public class CreatureTeamTests
{
    [Fact]
    public void StreakCountsBackFromTodayOrYesterday()
    {
        var today = new DateOnly(2026, 11, 27);
        var days = new Dictionary<DateOnly, int> { [today.AddDays(-1)] = 3, [today.AddDays(-2)] = 1, [today.AddDays(-3)] = 0, [today.AddDays(-4)] = 5 };
        Assert.Equal(2, TeamProgress.Streak(days, today));
        days[today] = 1;
        Assert.Equal(3, TeamProgress.Streak(days, today));
        Assert.Equal(0, TeamProgress.Streak(new Dictionary<DateOnly, int>(), today));
        Assert.Equal(1.2, TeamProgress.Multiplier(2), 6);
        Assert.Equal(2.0, TeamProgress.Multiplier(40), 6);
    }

    [Fact]
    public void ExperienceGoesToEveryoneOrOnlyTheLeader()
    {
        var state = new TeamState { Members = { new TeamMember { Id = "a" }, new TeamMember { Id = "b" } } };
        TeamProgress.Gain(state, 100, 0);
        Assert.Equal(75, state.Members[0].Xp, 6);
        Assert.Equal(75, state.Members[1].Xp, 6);
        TeamProgress.Gain(state, 100, 0); // Nothing new.
        Assert.Equal(75, state.Members[0].Xp, 6);
        state.ExpShare = false; state.Leader = 1;
        TeamProgress.Gain(state, 110, 5);
        Assert.Equal(75, state.Members[0].Xp, 6);
        Assert.Equal(75 + 10 * 1.5 * 3, state.Members[1].Xp, 6);
        Assert.True(TeamProgress.Level(state.Members[1].Xp) > TeamProgress.Level(state.Members[0].Xp));
        Assert.InRange(TeamProgress.Into(state.Members[0].Xp), 0, 1);
    }

    [Fact]
    public void PackListRefusesUnsafeNames()
    {
        var creatures = CreaturePack.Parse("""
            { "creatures": [
              { "id": "mossbun", "name": "Mossbun", "types": ["Leaf"], "frames": ["mossbun-0.png", "mossbun-1.png"] },
              { "id": "../evil", "name": "Evil", "frames": ["a.png"] },
              { "id": "sneaky", "name": "Sneaky", "frames": ["..\\..\\x.png"] },
              { "id": "noframes", "name": "None", "frames": [] }
            ] }
            """);
        var only = Assert.Single(creatures);
        Assert.Equal("mossbun", only.Id);
        Assert.Equal(new[] { "Leaf" }, only.Types);
    }

    [Fact]
    public void BelieOwnPackIsValid()
    {
        var root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "creatures", "pack.json"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        var creatures = CreaturePack.Parse(File.ReadAllText(Path.Combine(root!, "creatures", "pack.json")));
        Assert.Equal(7, creatures.Length);
        Assert.Equal("gliscor", creatures[0].Id); // First, so new teams include it.
        Assert.Equal("hang", creatures[0].Motion);
        foreach (var frame in creatures.SelectMany(creature => creature.Frames))
            Assert.True(File.Exists(Path.Combine(root!, "creatures", frame)), frame);
    }

    [Fact]
    public void PackColorsKeepOnlyPlainHexColors()
    {
        var colors = CreaturePack.ParseColors("""
            {"colors": {"Fire": "#EE8130", "ghost": "#735797", "Bad": "red", "Short": "#FFF", "Script": "#123456;x",
                        "Number": 5, "": "#000000", "AVeryLongTypeNameIndeed": "#111111"}, "creatures": []}
            """);
        Assert.Equal(2, colors.Count);
        Assert.Equal("#EE8130", colors["fire"]);
        Assert.Equal("#735797", colors["Ghost"]);
        Assert.Empty(CreaturePack.ParseColors("""{"creatures": []}"""));
        Assert.Empty(CreaturePack.ParseColors("""{"colors": ["#EE8130"]}"""));
    }

    [Fact]
    public void ContributionsAreReadFromTheGraphQlAnswer()
    {
        var count = GitHubContributions.Parse("""
            {"data":{"viewer":{"login":"someone","contributionsCollection":{"contributionCalendar":{"totalContributions":193,
            "weeks":[{"contributionDays":[{"date":"2026-11-26","contributionCount":2},{"date":"2026-11-27","contributionCount":0}]}]}}}}}
            """);
        Assert.Equal("someone", count.Login);
        Assert.Equal(193, count.Total);
        Assert.Equal(2, count.Days[new DateOnly(2026, 11, 26)]);
    }

    [Fact]
    public void CommitsCountWhenTheCalendarIsEmpty()
    {
        var count = GitHubContributions.ParseCommits("""
            aaa 2026-11-26T12:00:00+05:30
            bbb 2026-11-26T13:00:00+05:30
            aaa 2026-11-26T12:00:00+05:30
            ccc 2026-11-20T09:00:00Z
            not-a-line
            """, "someone");
        Assert.Equal("someone", count.Login);
        Assert.Equal(3, count.Total); // The repeated commit counts once.
        Assert.Equal(2, count.Days[DateOnly.FromDateTime(DateTimeOffset.Parse("2026-11-26T12:00:00+05:30").LocalDateTime)]);
        Assert.Equal(0, GitHubContributions.ParseCommits("", "someone").Total);
    }

    [Fact]
    public void AnyAccountCanBeCounted()
    {
        var named = GitHubContributions.Parse("""
            {"data":{"user":{"login":"Other-Person","contributionsCollection":{"contributionCalendar":{"totalContributions":5,
            "weeks":[{"contributionDays":[{"date":"2026-11-26","contributionCount":5}]}]}}}}}
            """);
        Assert.Equal("Other-Person", named.Login);
        Assert.Equal(5, named.Total);
        Assert.Throws<InvalidOperationException>(() => GitHubContributions.Parse("""{"data":{"user":null}}"""));
        Assert.True(GitHubContributions.IsValidAccount("Other-Person"));
        Assert.False(GitHubContributions.IsValidAccount("a\"){viewer"));
        Assert.False(GitHubContributions.IsValidAccount("-leading"));
        Assert.False(GitHubContributions.IsValidAccount(""));
    }

    [Fact]
    public void PublicCalendarIsRead()
    {
        // The shape of github.com/users/NAME/contributions.
        var page = GitHubContributions.ParsePage("""
            <td tabindex="0" data-ix="0" style="width: 10px" data-date="2026-11-25" id="contribution-day-component-0-0" data-level="1" role="gridcell" class="ContributionCalendar-day"></td>
            <td tabindex="0" data-ix="1" data-date="2026-11-26" id="contribution-day-component-0-1" data-level="0" class="ContributionCalendar-day"></td>
            <td tabindex="0" data-ix="2" data-date="2026-11-27" id="contribution-day-component-0-2" data-level="4" class="ContributionCalendar-day"></td>
            <tool-tip id="tooltip-a" for="contribution-day-component-0-0" popover="manual" class="sr-only">7 contributions on November 25th.</tool-tip>
            <tool-tip id="tooltip-b" for="contribution-day-component-0-1" popover="manual" class="sr-only">No contributions on November 26th.</tool-tip>
            <tool-tip id="tooltip-c" for="contribution-day-component-0-2" popover="manual" class="sr-only">1,204 contributions on November 27th.</tool-tip>
            """, "someone");
        Assert.Equal(7 + 1204, page.Total);
        Assert.Equal(0, page.Days[new DateOnly(2026, 11, 26)]);
        Assert.Equal(1, TeamProgress.Streak(page.Days, new DateOnly(2026, 11, 27)));
        Assert.Throws<InvalidDataException>(() => GitHubContributions.ParsePage("<html>changed</html>", "someone"));
    }

    /// <summary>Reads a real public calendar. Does nothing unless BELIE_GITHUB_LIVE names an account.</summary>
    [Fact]
    public async Task RealPublicCalendarIsRead()
    {
        var login = Environment.GetEnvironmentVariable("BELIE_GITHUB_LIVE");
        if (string.IsNullOrEmpty(login)) return;
        using var http = new System.Net.Http.HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Belie (test)");
        var page = GitHubContributions.ParsePage(await http.GetStringAsync($"https://github.com/users/{login}/contributions"), login);
        Assert.InRange(page.Days.Count, 360, 372);
        Assert.True(page.Total > 0);
        var cli = await GitHubContributions.ReadAsync(login, CancellationToken.None);
        Assert.Equal(cli.Total, page.Total);
    }

    [Fact]
    public void CutoutLandsWhereThePictureDoes()
    {
        const int side = SubjectModel.MapSize;
        var map = new byte[side * side];
        // A subject in the middle of the picture.
        for (var y = side * 3 / 10; y < side * 9 / 10; y++)
            for (var x = side * 4 / 10; x < side * 6 / 10; x++) map[y * side + x] = 255;
        var whole = SubjectFinder.FromCutout(map, new RectangleF(0, 0, 1, 1), 192, 108);
        Assert.NotNull(whole);
        Assert.True(whole![60 * 192 + 96]);
        Assert.False(whole[60 * 192 + 20]);
        // The picture shifted right by a quarter of the screen: so is the subject.
        var shifted = SubjectFinder.FromCutout(map, new RectangleF(.25f, 0, 1, 1), 192, 108);
        Assert.NotNull(shifted);
        Assert.True(shifted![60 * 192 + 96 + 48]);
        Assert.False(shifted[60 * 192 + 80]);
        // A "subject" that is the whole picture is refused.
        Assert.Null(SubjectFinder.FromCutout(Enumerable.Repeat((byte)255, side * side).ToArray(), new RectangleF(0, 0, 1, 1), 192, 108));
        Assert.Null(SubjectFinder.FromCutout(new byte[side * side], new RectangleF(0, 0, 1, 1), 192, 108));
        // A car in the top-right corner runs off two edges of the screen but is still one thing.
        var corner = new byte[side * side];
        for (var y = 0; y < side * 9 / 10; y++)
            for (var x = side * 7 / 10; x < side; x++) corner[y * side + x] = 255;
        Assert.NotNull(SubjectFinder.FromCutout(corner, new RectangleF(0, 0, 1, 1), 192, 108));
        // Hills along the top and both sides are scenery.
        var band = new byte[side * side];
        for (var y = 0; y < side * 2 / 5; y++)
            for (var x = 0; x < side; x++) band[y * side + x] = 255;
        Assert.Null(SubjectFinder.FromCutout(band, new RectangleF(0, 0, 1, 1), 192, 108));
    }

    [Fact]
    public void ModelScoresBecomeAStretchedMap()
    {
        var logits = new float[64 * 64];
        for (var i = 0; i < logits.Length; i++) logits[i] = i % 64 < 32 ? -6 : 6;
        var map = SubjectModel.Shrink(logits, 64);
        Assert.Equal(SubjectModel.MapSize * SubjectModel.MapSize, map.Length);
        Assert.Equal(0, map[10]);
        Assert.Equal(255, map[SubjectModel.MapSize - 10]);
    }
}
