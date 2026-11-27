using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;

namespace WallpaperProfiles.Engine;

/// <summary>Levels, badges and streaks: pure arithmetic, so it can be tested.</summary>
internal static class TeamProgress
{
    public const int Size = 6;

    public static readonly (int Contributions, string Name)[] Badges =
    {
        (10, "Spark"), (50, "Leaf"), (100, "Tide"), (250, "Stone"), (500, "Ember"), (1000, "Frost"), (2000, "Storm"), (3500, "Rainbow")
    };

    /// <summary>Each day of the streak adds a tenth, up to double.</summary>
    public static double Multiplier(int streak) => 1 + Math.Min(10, Math.Max(0, streak)) / 10.0;

    public static int Level(double xp) => 1 + (int)Math.Sqrt(Math.Max(0, xp) / 6);
    public static double XpFor(int level) => 6.0 * (level - 1) * (level - 1);

    /// <summary>How far into its level a creature is, from 0 to 1.</summary>
    public static double Into(double xp)
    {
        var level = Level(xp);
        double from = XpFor(level), to = XpFor(level + 1);
        return (xp - from) / (to - from);
    }

    public static int Earned(int contributions) => Badges.Count(badge => contributions >= badge.Contributions);

    /// <summary>Days in a row with a contribution, counting back from today, or from yesterday when today has none yet.</summary>
    public static int Streak(IReadOnlyDictionary<DateOnly, int> days, DateOnly today)
    {
        var day = days.TryGetValue(today, out var now) && now > 0 ? today : today.AddDays(-1);
        var streak = 0;
        while (days.TryGetValue(day, out var count) && count > 0) { streak++; day = day.AddDays(-1); }
        return streak;
    }

    /// <summary>Hands out the experience from contributions made since the last count.</summary>
    public static void Gain(TeamState state, int total, int streak)
    {
        // The first count only sets where the team starts, as if it had been there all year.
        var gained = state.LastTotal < 0 ? total : Math.Max(0, total - state.LastTotal);
        state.LastTotal = total; state.Streak = streak;
        if (gained == 0 || state.Members.Count == 0) return;
        var xp = gained * Multiplier(streak);
        if (state.ExpShare) foreach (var member in state.Members) member.Xp += xp / state.Members.Count * 1.5;
        else state.Members[Math.Clamp(state.Leader, 0, state.Members.Count - 1)].Xp += xp * 3;
    }
}

/// <summary>
/// Counts contributions with the GitHub CLI, so Belie never holds a token of its own. A named account can also be
/// counted without the CLI, from its public contribution calendar.
/// </summary>
internal static class GitHubContributions
{
    private const string Days = "contributionsCollection{contributionCalendar{totalContributions weeks{contributionDays{date contributionCount}}}}";
    private static readonly Regex Account = new("\\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})\\z");

    public sealed record Count(string Login, int Total, IReadOnlyDictionary<DateOnly, int> Days);

    public static bool IsValidAccount(string login) => Account.IsMatch(login);

    /// <param name="login">Empty for the account signed in to the GitHub CLI.</param>
    public static async Task<Count> ReadAsync(string login, CancellationToken token)
    {
        login = login.Trim().TrimStart('@');
        if (login.Length > 0 && !IsValidAccount(login)) throw new ArgumentException($"\"{login}\" is not a GitHub account name.");
        try
        {
            var count = Parse(await Cli(new[] { "api", "graphql", "-f", "query=" + (login.Length == 0 ? "query{viewer{login " + Days + "}}" : "query{user(login:\"" + login + "\"){login " + Days + "}}") }, token));
            return count.Total > 0 ? count : await FromCommitsAsync(count, cli: true, token);
        }
        catch (CliMissingException) when (login.Length > 0)
        {
            var count = ParsePage(await Page(login, token), login);
            return count.Total > 0 ? count : await FromCommitsAsync(count, cli: false, token);
        }
        catch (CliMissingException)
        {
            throw new InvalidOperationException("Type your GitHub account name below, or install the GitHub CLI (gh) and run gh auth login.");
        }
    }

    private sealed class CliMissingException : Exception { }

    /// <summary>
    /// Some accounts' contribution calendars stay empty although their commits are there (GitHub leaves them out). Then
    /// the commits themselves are counted, from GitHub's commit search, so the team still grows. A calendar that is
    /// really empty stays empty: the search finds nothing either.
    /// </summary>
    private static async Task<Count> FromCommitsAsync(Count calendar, bool cli, CancellationToken token)
    {
        if (calendar.Login.Length == 0 || !IsValidAccount(calendar.Login)) return calendar;
        var since = DateOnly.FromDateTime(DateTime.Today.AddYears(-1)).ToString("yyyy-MM-dd");
        var query = $"author:{calendar.Login} author-date:>={since}";
        try
        {
            string lines;
            if (cli)
                lines = await Cli(new[] { "api", "-X", "GET", "search/commits", "-f", "q=" + query, "-f", "per_page=100", "--paginate",
                    "--jq", ".items[] | .sha + \" \" + .commit.author.date" }, token);
            else
            {
                // Without the CLI, GitHub allows a few searches an hour; the first page is enough for a start.
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Belie (team)");
                var json = await http.GetStringAsync("https://api.github.com/search/commits?per_page=100&q=" + Uri.EscapeDataString(query), token);
                using var document = JsonDocument.Parse(json);
                lines = string.Join("\n", document.RootElement.GetProperty("items").EnumerateArray()
                    .Select(item => item.GetProperty("sha").GetString() + " " + item.GetProperty("commit").GetProperty("author").GetProperty("date").GetString()));
            }
            var commits = ParseCommits(lines, calendar.Login);
            if (commits.Total > 0) Logger.Info($"The contribution calendar is empty; counted {commits.Total} commits instead.");
            return commits.Total > 0 ? commits : calendar;
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException or KeyNotFoundException or CliMissingException)
        {
            Logger.Warn("Counting commits failed: " + ex.Message);
            return calendar;
        }
    }

    /// <summary>Reads "sha date" lines; each commit counts once, on the day it was written, in local time.</summary>
    internal static Count ParseCommits(string lines, string login)
    {
        var seen = new HashSet<string>();
        var days = new Dictionary<DateOnly, int>();
        foreach (var line in lines.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', 2);
            if (parts.Length != 2 || !seen.Add(parts[0])
                || !DateTimeOffset.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var when)) continue;
            var day = DateOnly.FromDateTime(when.LocalDateTime);
            days[day] = days.GetValueOrDefault(day) + 1;
        }
        return new Count(login, days.Values.Sum(), days);
    }

    private static async Task<string> Cli(IEnumerable<string> arguments, CancellationToken token)
    {
        var start = new ProcessStartInfo("gh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        Process process;
        try { process = Process.Start(start) ?? throw new CliMissingException(); }
        catch (System.ComponentModel.Win32Exception) { throw new CliMissingException(); }
        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync(token);
            var errors = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            var answer = await output;
            if (process.ExitCode == 0) return answer;
            var message = (await errors).Trim();
            if (message.Contains("auth login", StringComparison.OrdinalIgnoreCase)) throw new CliMissingException();
            throw new InvalidOperationException((answer + message).Contains("Could not resolve to a User", StringComparison.OrdinalIgnoreCase)
                ? "GitHub has no account by that name." : "GitHub did not answer: " + message);
        }
    }

    private static async Task<string> Page(string login, CancellationToken token)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Belie (team)");
        using var response = await http.GetAsync("https://github.com/users/" + login + "/contributions", token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) throw new InvalidOperationException("GitHub has no account by that name.");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(token);
    }

    internal static Count Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement.GetProperty("data");
        var person = data.TryGetProperty("viewer", out var viewer) ? viewer : data.GetProperty("user");
        if (person.ValueKind == JsonValueKind.Null) throw new InvalidOperationException("GitHub has no account by that name.");
        var calendar = person.GetProperty("contributionsCollection").GetProperty("contributionCalendar");
        var days = new Dictionary<DateOnly, int>();
        foreach (var week in calendar.GetProperty("weeks").EnumerateArray())
            foreach (var day in week.GetProperty("contributionDays").EnumerateArray())
                days[DateOnly.Parse(day.GetProperty("date").GetString()!)] = day.GetProperty("contributionCount").GetInt32();
        return new Count(person.GetProperty("login").GetString() ?? "", calendar.GetProperty("totalContributions").GetInt32(), days);
    }

    /// <summary>Reads the calendar on an account's public page: each day's cell, and the label that says its count.</summary>
    internal static Count ParsePage(string html, string login)
    {
        var dates = new Dictionary<string, DateOnly>();
        foreach (Match cell in Regex.Matches(html, "<td\\b[^>]*>"))
        {
            var date = Regex.Match(cell.Value, "\\bdata-date=\"(\\d{4}-\\d{2}-\\d{2})\"");
            var id = Regex.Match(cell.Value, "\\bid=\"([^\"]+)\"");
            if (date.Success && id.Success) dates[id.Groups[1].Value] = DateOnly.Parse(date.Groups[1].Value);
        }
        if (dates.Count == 0) throw new InvalidDataException("GitHub's contribution page has changed; install the GitHub CLI to count instead.");
        var days = new Dictionary<DateOnly, int>();
        foreach (Match label in Regex.Matches(html, "<tool-tip\\b[^>]*\\bfor=\"([^\"]+)\"[^>]*>\\s*(No|[\\d,]+) contributions?"))
            if (dates.TryGetValue(label.Groups[1].Value, out var date))
                days[date] = label.Groups[2].Value == "No" ? 0 : int.Parse(label.Groups[2].Value.Replace(",", ""));
        return new Count(login, days.Values.Sum(), days);
    }
}

internal sealed record Creature(string Id, string Name, string[] Types, string[] Frames);

/// <summary>
/// The creatures' names and sprites are not in Belie: they are a pack downloaded once, from Belie's repository unless
/// another pack is named. A pack is a pack.json next to its PNG frames.
/// </summary>
internal static class CreaturePack
{
    public const string DefaultUrl = "https://raw.githubusercontent.com/el-rato/Belie/main/creatures/pack.json";
    private const int MaxFrameBytes = 512 * 1024, MaxCreatures = 64;
    private static readonly Regex Safe = new("\\A[a-z0-9][a-z0-9_-]{0,40}\\z");
    private static readonly Regex SafeFile = new("\\A[a-z0-9][a-z0-9_-]{0,60}\\.png\\z");

    public static string Folder => Path.Combine(AppPaths.BaseDir, "team", "pack");
    public static string StateFile => Path.Combine(AppPaths.BaseDir, "team", "team.json");

    public static TeamState LoadState()
    {
        try { return File.Exists(StateFile) ? AtomicJsonFile.Read<TeamState>(StateFile) ?? new TeamState() : new TeamState(); }
        catch (Exception ex) { Logger.Warn("The team could not be read: " + ex.Message); return new TeamState(); }
    }

    public static void SaveState(TeamState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
        AtomicJsonFile.Write(StateFile, state);
    }

    /// <summary>The installed creatures; empty before the pack is downloaded.</summary>
    public static IReadOnlyList<Creature> Installed()
    {
        try
        {
            var file = Path.Combine(Folder, "pack.json");
            return File.Exists(file) ? Parse(File.ReadAllText(file)).Where(creature => creature.Frames.All(frame => File.Exists(Path.Combine(Folder, frame)))).ToArray() : Array.Empty<Creature>();
        }
        catch (Exception ex) { Logger.Warn("The creature pack could not be read: " + ex.Message); return Array.Empty<Creature>(); }
    }

    public static string FramePath(string frame) => Path.Combine(Folder, frame);

    /// <summary>The installed pack's colors for its types, such as "Fire": "#F08030". Empty when it names none.</summary>
    public static IReadOnlyDictionary<string, string> InstalledColors()
    {
        try
        {
            var file = Path.Combine(Folder, "pack.json");
            return File.Exists(file) ? ParseColors(File.ReadAllText(file)) : new Dictionary<string, string>();
        }
        catch (Exception ex) { Logger.Warn("The creature pack's colors could not be read: " + ex.Message); return new Dictionary<string, string>(); }
    }

    /// <summary>Only "#RRGGBB" values for type names a creature could have; anything else is left out.</summary>
    internal static Dictionary<string, string> ParseColors(string json)
    {
        using var document = JsonDocument.Parse(json);
        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!document.RootElement.TryGetProperty("colors", out var list) || list.ValueKind != JsonValueKind.Object) return colors;
        foreach (var entry in list.EnumerateObject().Take(64))
            if (entry.Name.Length is > 0 and <= 16 && entry.Value.ValueKind == JsonValueKind.String && entry.Value.GetString() is { } color && HexColor.IsMatch(color))
                colors[entry.Name] = color;
        return colors;
    }

    private static readonly Regex HexColor = new("\\A#[0-9A-Fa-f]{6}\\z");

    internal static Creature[] Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var creatures = new List<Creature>();
        foreach (var item in document.RootElement.GetProperty("creatures").EnumerateArray().Take(MaxCreatures))
        {
            var id = item.GetProperty("id").GetString() ?? "";
            var frames = item.GetProperty("frames").EnumerateArray().Select(frame => frame.GetString() ?? "").ToArray();
            if (!Safe.IsMatch(id) || frames.Length is 0 or > 8 || !frames.All(SafeFile.IsMatch)) continue;
            var name = (item.GetProperty("name").GetString() ?? id).Trim();
            var types = item.TryGetProperty("types", out var list) ? list.EnumerateArray().Select(type => type.GetString() ?? "").Where(type => type.Length is > 0 and <= 16).Take(2).ToArray() : Array.Empty<string>();
            creatures.Add(new Creature(id, name.Length > 24 ? name[..24] : name, types, frames));
        }
        return creatures.ToArray();
    }

    /// <summary>Downloads a pack and its frames into a fresh folder, then puts it in place of the old one.</summary>
    public static async Task<int> DownloadAsync(string url, IProgress<double> progress, CancellationToken token)
    {
        if (!Uri.TryCreate(string.IsNullOrWhiteSpace(url) ? DefaultUrl : url.Trim(), UriKind.Absolute, out var manifest) || manifest.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("A creature pack's address must start with https://.");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Belie (creature pack)");
        var json = await http.GetStringAsync(manifest, token);
        if (json.Length > 256 * 1024) throw new InvalidDataException("The pack's list is too large.");
        var creatures = Parse(json);
        if (creatures.Length == 0) throw new InvalidDataException("The pack has no creatures Belie can use.");
        var staging = Folder + ".new";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        var frames = creatures.SelectMany(creature => creature.Frames).Distinct().ToArray();
        for (var i = 0; i < frames.Length; i++)
        {
            var bytes = await http.GetByteArrayAsync(new Uri(manifest, frames[i]), token);
            // Only small PNGs: the signature, and a size a sprite never needs to pass.
            if (bytes.Length > MaxFrameBytes || bytes.Length < 8 || bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47)
                throw new InvalidDataException($"{frames[i]} is not a small PNG.");
            await File.WriteAllBytesAsync(Path.Combine(staging, frames[i]), bytes, token);
            progress.Report((i + 1) / (double)frames.Length);
        }
        await File.WriteAllTextAsync(Path.Combine(staging, "pack.json"), json, token);
        if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
        Directory.Move(staging, Folder);
        return creatures.Length;
    }

    /// <summary>Fills empty places in the team from the pack, and drops members the pack no longer has.</summary>
    public static void Fill(TeamState state, IReadOnlyList<Creature> creatures)
    {
        var known = creatures.Select(creature => creature.Id).ToHashSet();
        state.Members.RemoveAll(member => !known.Contains(member.Id));
        foreach (var creature in creatures)
        {
            if (state.Members.Count >= TeamProgress.Size) break;
            if (state.Members.All(member => member.Id != creature.Id)) state.Members.Add(new TeamMember { Id = creature.Id });
        }
        state.Leader = Math.Clamp(state.Leader, 0, Math.Max(0, state.Members.Count - 1));
    }
}
