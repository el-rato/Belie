using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Media;
using WallpaperProfiles.Persistence;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using ComboBox = System.Windows.Controls.ComboBox;
using IDataObject = System.Windows.IDataObject;
using DataFormats = System.Windows.DataFormats;

namespace WallpaperProfiles.Engine;

internal static class DesktopCanvasFeatures
{
    private static string CopyAsset(DesktopCanvasStore store, string path, string folder, Guid id)
    {
        var directory = Path.Combine(store.FilePath + ".assets", folder);
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, id.ToString("N") + "-" + Guid.NewGuid().ToString("N") + Path.GetExtension(path));
        File.Copy(path, destination);
        return destination;
    }

    public static IReadOnlyList<DesktopWidget> ReadDrop(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
        {
            if (files.Length > 32) throw new InvalidOperationException("Drop up to 32 images at a time.");
            if (files.Any(file => !File.Exists(file) || !WallpaperEngine.IsImageFile(file)))
                throw new InvalidOperationException("Drop image files, website addresses, or note text.");
            return files.Select(file => new DesktopWidget { Kind = DesktopWidgetKind.Image, Title = Path.GetFileNameWithoutExtension(file), Content = file }).ToArray();
        }
        var text = data.GetData(DataFormats.UnicodeText) as string ?? data.GetData(DataFormats.Text) as string;
        if (string.IsNullOrWhiteSpace(text) && data.GetData("UniformResourceLocatorW") is Stream urlStream)
        {
            using var reader = new StreamReader(urlStream, Encoding.Unicode, leaveOpen: true);
            text = reader.ReadToEnd().TrimEnd('\0');
        }
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Drop image files, website addresses, or note text.");
        if (text.Length > 100000) throw new InvalidOperationException("This note is too long. Drop a smaller selection.");
        var link = DesktopWidget.TryGetLink(text.Trim(), out var uri);
        return new[] { new DesktopWidget { Kind = link ? DesktopWidgetKind.Link : DesktopWidgetKind.Note,
            Title = link ? uri!.Host : "Dropped note", Content = link ? text.Trim() : text,
            LeetCodeUsername = link && uri!.Host.Equals("leetcode.com", StringComparison.OrdinalIgnoreCase) ? "_Unkillable__Demon_King" : "" } };
    }

    public static void SaveDrop(DesktopCanvasStore store, IReadOnlyList<DesktopWidget> widgets, double x, double y)
    {
        foreach (var widget in widgets.Where(item => item.Kind == DesktopWidgetKind.Image))
            widget.Content = CopyAsset(store, widget.Content, "images", widget.Id);
        store.Update(document =>
        {
            for (var i = 0; i < widgets.Count; i++)
            {
                widgets[i].X = x + i * 24; widgets[i].Y = y + i * 24;
                document.Widgets.Add(widgets[i]);
            }
            document.Enabled = true;
        });
    }
}

internal sealed class DesktopSketch : Grid
{
    private readonly InkCanvas _ink = new() { Background = Brushes.Transparent };
    private readonly Action<string, double, double> _save;
    private string _drawing;
    private bool _loading;
    private double _width, _height;
    private readonly List<StrokeCollection> _undo = new();
    public string Drawing => _drawing;

    public DesktopSketch(DesktopWidget widget, Action<string, double, double> save, Action? moveStart = null, Action? move = null, Action? moveEnd = null)
    {
        _save = save; _drawing = widget.Drawing; _width = widget.DrawingWidth; _height = widget.DrawingHeight;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition());
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 5) };
        void Tool(string title, Action action)
        {
            var button = new Button { Content = title, Padding = new Thickness(5, 3, 5, 3), Margin = new Thickness(0, 0, 3, 3), FontSize = 11 };
            button.Click += (_, _) => action(); tools.Children.Add(button);
        }
        Tool("Draw", () => _ink.EditingMode = InkCanvasEditingMode.Ink);
        Tool("Erase", () => _ink.EditingMode = InkCanvasEditingMode.EraseByStroke);
        Tool("Undo", () =>
        {
            if (_undo.Count == 0) return;
            _loading = true; _ink.Strokes = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); _loading = false; Persist();
        });
        Tool("Clear", () => { _undo.Add(_ink.Strokes.Clone()); _ink.Strokes.Clear(); });
        var handle = new Thumb { Width = 26, Height = 24, IsEnabled = !widget.Locked, Cursor = System.Windows.Input.Cursors.SizeAll, ToolTip = "Drag to move sketch", Margin = new Thickness(0, 0, 3, 3) };
        var moveTemplate = new ControlTemplate(typeof(Thumb));
        var moveText = new FrameworkElementFactory(typeof(TextBlock)); moveText.SetValue(TextBlock.TextProperty, "✥");
        moveText.SetValue(TextBlock.BackgroundProperty, Brushes.Transparent); moveText.SetValue(TextBlock.FontSizeProperty, 18d);
        moveText.SetValue(TextBlock.TextAlignmentProperty, System.Windows.TextAlignment.Center); moveTemplate.VisualTree = moveText; handle.Template = moveTemplate;
        handle.DragStarted += (_, _) => moveStart?.Invoke(); handle.DragDelta += (_, _) => move?.Invoke(); handle.DragCompleted += (_, _) => moveEnd?.Invoke();
        tools.Children.Add(handle);
        var color = new ComboBox { Width = 80, FontSize = 11, ItemsSource = new[] { widget.TextColor, "White", "Black", "Gold", "Crimson", "Cyan", "Lime" }, SelectedIndex = 0 };
        color.SelectionChanged += (_, _) => _ink.DefaultDrawingAttributes.Color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(color.SelectedItem.ToString()!);
        tools.Children.Add(color);
        var size = new ComboBox { Width = 44, FontSize = 11, ItemsSource = new[] { 2, 4, 8, 12 }, SelectedIndex = 1 };
        size.SelectionChanged += (_, _) => { _ink.DefaultDrawingAttributes.Width = _ink.DefaultDrawingAttributes.Height = (int)size.SelectedItem; };
        tools.Children.Add(size);
        Children.Add(tools); Grid.SetRow(_ink, 1); Children.Add(_ink);
        _ink.DefaultDrawingAttributes = new DrawingAttributes { Color = SceneController.TryParseAccent(widget.TextColor, out var inkColor) ? inkColor : Colors.White, Width = 4, Height = 4, FitToCurve = true };
        _ink.EditingMode = InkCanvasEditingMode.Ink;
        LoadDrawing();
        _ink.Strokes.StrokesChanged += Changed;
        _ink.SizeChanged += (_, _) =>
        {
            if (_ink.ActualWidth <= 0 || _ink.ActualHeight <= 0) return;
            _loading = true;
            var matrix = Matrix.Identity; matrix.Scale(_ink.ActualWidth / _width, _ink.ActualHeight / _height);
            _ink.Strokes.Transform(matrix, false); _width = _ink.ActualWidth; _height = _ink.ActualHeight;
            _loading = false;
        };
        // Replacing Strokes changes the event source; subscribe to each replacement for undo.
        _ink.StrokesReplaced += (_, _) => { _ink.Strokes.StrokesChanged += Changed; };
        _ink.PreviewMouseDown += (_, _) =>
        {
            _undo.Add(_ink.Strokes.Clone()); if (_undo.Count > 20) _undo.RemoveAt(0);
        };
    }
    public void UpdateDrawing(DesktopWidget widget)
    {
        if (widget.Drawing == _drawing) return;
        _loading = true; _drawing = widget.Drawing; _width = widget.DrawingWidth; _height = widget.DrawingHeight;
        _ink.Strokes = new StrokeCollection(); LoadDrawing(); _undo.Clear();
        if (_ink.ActualWidth > 0 && _ink.ActualHeight > 0)
        {
            var matrix = Matrix.Identity; matrix.Scale(_ink.ActualWidth / _width, _ink.ActualHeight / _height);
            _ink.Strokes.Transform(matrix, false); _width = _ink.ActualWidth; _height = _ink.ActualHeight;
        }
        _loading = false;
    }
    private void LoadDrawing()
    {
        if (string.IsNullOrEmpty(_drawing)) return;
        try { using var stream = new MemoryStream(Convert.FromBase64String(_drawing)); _ink.Strokes = new StrokeCollection(stream); }
        catch { ToolTip = "This drawing could not be loaded. Draw again or clear it."; }
    }
    private void Changed(object? sender, StrokeCollectionChangedEventArgs e) { if (!_loading) Persist(); }
    private void Persist()
    {
        using var stream = new MemoryStream(); _ink.Strokes.Save(stream);
        var drawing = Convert.ToBase64String(stream.ToArray());
        if (drawing.Length > 6000000) { ToolTip = "Drawing is full. Clear some strokes before saving."; return; }
        _drawing = drawing; _save(drawing, _width, _height);
    }
}

internal static class DesktopLinkService
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(15) };
    public static async Task RefreshAsync(DesktopCanvasStore store, DesktopWidget widget)
    {
        if (!DesktopWidget.TryGetLink(widget.Content, out var uri)) return;
        try
        {
            if (widget.IsLeetCode) await RefreshLeetCode(store, widget);
            else
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd("Belie/2.1");
                using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentType?.MediaType is not ("text/html" or "application/xhtml+xml"))
                    throw new InvalidOperationException("Preview unavailable for this page.");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var output = new MemoryStream(); var buffer = new byte[8192];
                while (output.Length < 262144)
                {
                    var count = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, 262144 - output.Length)), timeout.Token);
                    if (count == 0) break; output.Write(buffer, 0, count);
                }
                var html = Encoding.UTF8.GetString(output.ToArray()); var metadata = ParseMetadata(html);
                Commit(store, widget, saved => { saved.LinkPageTitle = metadata.Title; saved.LinkDescription = metadata.Description; saved.LinkStatus = ""; });
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or FormatException)
        {
            Commit(store, widget, saved => saved.LinkStatus = ex is InvalidOperationException ? ex.Message : "Offline or unavailable · showing saved details");
        }
    }
    private static void Commit(DesktopCanvasStore store, DesktopWidget original, Action<DesktopWidget> update)
    {
        store.UpdateWidget(original.Id, saved =>
        {
            if (saved.Content != original.Content || saved.LeetCodeUsername != original.LeetCodeUsername) return;
            update(saved); saved.LinkUpdatedAt = DateTimeOffset.UtcNow;
        });
    }
    public static (string Title, string Description) ParseMetadata(string html)
    {
        const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.Singleline;
        string Clean(string value, int limit) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(value, "<[^>]+>", "", options, TimeSpan.FromSeconds(1))), @"\s+", " ").Trim() is var clean
            ? clean[..Math.Min(clean.Length, limit)] : "";
        var title = Regex.Match(html, @"<title\b[^>]*>(.*?)</title>", options, TimeSpan.FromSeconds(1));
        var description = ""; var pageTitle = title.Success ? Clean(title.Groups[1].Value, 160) : "";
        foreach (Match meta in Regex.Matches(html, @"<meta\b[^>]*>", options, TimeSpan.FromSeconds(1)))
        {
            var attributes = Regex.Matches(meta.Value, "([\\w:-]+)\\s*=\\s*([\"'])(.*?)\\2", options, TimeSpan.FromSeconds(1))
                .Cast<Match>().GroupBy(match => match.Groups[1].Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Groups[3].Value, StringComparer.OrdinalIgnoreCase);
            var name = attributes.GetValueOrDefault("name") ?? attributes.GetValueOrDefault("property");
            if (!attributes.TryGetValue("content", out var content)) continue;
            if (name?.Equals("og:title", StringComparison.OrdinalIgnoreCase) == true) pageTitle = Clean(content, 160);
            if (name?.ToLowerInvariant() is "description" or "og:description") description = Clean(content, 350);
        }
        return (pageTitle, description);
    }
    private static async Task<JsonDocument> Query(string query, object variables)
    {
        using var content = new StringContent(JsonSerializer.Serialize(new { query, variables }), Encoding.UTF8, "application/json");
        using var response = await Client.PostAsync("https://leetcode.com/graphql/", content);
        response.EnsureSuccessStatusCode();
        var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (result.RootElement.TryGetProperty("errors", out _)) { result.Dispose(); throw new InvalidOperationException("LeetCode public activity is temporarily unavailable."); }
        return result;
    }
    private static async Task RefreshLeetCode(DesktopCanvasStore store, DesktopWidget widget)
    {
        if (string.IsNullOrWhiteSpace(widget.LeetCodeUsername)) throw new InvalidOperationException("Add your LeetCode username in Belie.");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        const string query = "query($username:String!,$year:Int!){ activeDailyCodingChallengeQuestion { date link question { title titleSlug } } matchedUser(username:$username){userCalendar(year:$year){submissionCalendar}} recentAcSubmissionList(username:$username,limit:100){titleSlug timestamp} }";
        using var result = await Query(query, new { username = widget.LeetCodeUsername, year = today.Year });
        var data = result.RootElement.GetProperty("data");
        var user = data.GetProperty("matchedUser");
        if (user.ValueKind == JsonValueKind.Null) throw new InvalidOperationException("LeetCode username not found.");
        var daily = data.GetProperty("activeDailyCodingChallengeQuestion");
        var submissions = data.GetProperty("recentAcSubmissionList").EnumerateArray().Select(item =>
            (Slug: item.GetProperty("titleSlug").GetString()!, Day: DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(long.Parse(item.GetProperty("timestamp").GetString()!, CultureInfo.InvariantCulture)).UtcDateTime))).ToArray();
        var challenges = new Dictionary<DateOnly, string>();
        var dailyDate = DateOnly.Parse(daily.GetProperty("date").GetString()!, CultureInfo.InvariantCulture);
        challenges[dailyDate] = daily.GetProperty("question").GetProperty("titleSlug").GetString()!;
        foreach (var month in submissions.Select(item => new DateOnly(item.Day.Year, item.Day.Month, 1)).Distinct().OrderByDescending(day => day).Take(4))
        {
            using var archive = await Query("query($year:Int!,$month:Int!){dailyCodingChallengeV2(year:$year,month:$month){challenges{date question{titleSlug}}}}", new { year = month.Year, month = month.Month });
            foreach (var challenge in archive.RootElement.GetProperty("data").GetProperty("dailyCodingChallengeV2").GetProperty("challenges").EnumerateArray())
                challenges[DateOnly.Parse(challenge.GetProperty("date").GetString()!, CultureInfo.InvariantCulture)] = challenge.GetProperty("question").GetProperty("titleSlug").GetString()!;
        }
        using var calendar = JsonDocument.Parse(user.GetProperty("userCalendar").GetProperty("submissionCalendar").GetString() ?? "{}");
        var days = calendar.RootElement.EnumerateObject().Where(item => item.Value.GetInt32() > 0).Select(item =>
            DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(long.Parse(item.Name, CultureInfo.InvariantCulture)).UtcDateTime)).ToHashSet();
        if (today.Month == 1)
        {
            using var previous = await Query("query($username:String!,$year:Int!){matchedUser(username:$username){userCalendar(year:$year){submissionCalendar}}}", new { username = widget.LeetCodeUsername, year = today.Year - 1 });
            using var oldCalendar = JsonDocument.Parse(previous.RootElement.GetProperty("data").GetProperty("matchedUser").GetProperty("userCalendar").GetProperty("submissionCalendar").GetString() ?? "{}");
            foreach (var item in oldCalendar.RootElement.EnumerateObject().Where(item => item.Value.GetInt32() > 0))
                days.Add(DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(long.Parse(item.Name, CultureInfo.InvariantCulture)).UtcDateTime));
        }
        var activity = 0; var cursor = days.Contains(today) ? today : today.AddDays(-1);
        while (days.Contains(cursor)) { activity++; cursor = cursor.AddDays(-1); }
        var verified = VerifyPotd(submissions, challenges);
        Commit(store, widget, saved =>
        {
            saved.PotdCompletions = saved.PotdCompletions.Concat(verified).Distinct().Order().ToList();
            saved.PotdDate = dailyDate; saved.PotdTitle = daily.GetProperty("question").GetProperty("title").GetString() ?? "Today's challenge";
            saved.PotdUrl = "https://leetcode.com" + daily.GetProperty("link").GetString();
            saved.ActivityStreak = activity; saved.LinkStatus = "";
        });
    }
    public static IEnumerable<DateOnly> VerifyPotd(IEnumerable<(string Slug, DateOnly Day)> submissions, IReadOnlyDictionary<DateOnly, string> challenges)
        => submissions.Where(item => challenges.TryGetValue(item.Day, out var slug) && slug == item.Slug).Select(item => item.Day).Distinct();
}
