using System.Text.Json;

namespace WallpaperProfiles.Persistence;

internal enum DesktopWidgetKind { Note, Countdown, Image, Link, Sketch }
internal enum WidgetTextAlignment { Left, Center, Right }
internal enum WidgetImageFit { Fit, Fill, Stretch }

internal sealed class DesktopWidget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DesktopWidgetKind Kind { get; set; }
    public string Title { get; set; } = "New note";
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Untitled " + Kind.ToString().ToLowerInvariant() : Title;
    public string Content { get; set; } = "";
    public DateTimeOffset Target { get; set; } = DateTimeOffset.Now.AddDays(7);
    public double X { get; set; } = 160;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 230;
    public bool Enabled { get; set; } = true;
    public bool Locked { get; set; }
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 16;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public WidgetTextAlignment Alignment { get; set; }
    public string TextColor { get; set; } = "#F1F2EE";
    public string BackgroundColor { get; set; } = "#1D201E";
    public double Opacity { get; set; } = 1;
    public double CornerRadius { get; set; } = 12;
    public bool ShowHeader { get; set; } = true;
    public bool ShowBorder { get; set; } = true;
    public string BorderColor { get; set; } = "#738579";
    public double BorderWidth { get; set; } = 1;
    public bool ShowBackground { get; set; } = true;
    public bool GlassEffect { get; set; }
    public WidgetImageFit ImageFit { get; set; }
    public string Drawing { get; set; } = "";
    public double DrawingWidth { get; set; } = 300;
    public double DrawingHeight { get; set; } = 180;
    public string LinkDescription { get; set; } = "";
    public string LinkCustomDescription { get; set; } = "";
    public string LinkPageTitle { get; set; } = "";
    public string LeetCodeUsername { get; set; } = "";
    public List<DateOnly> PotdCompletions { get; set; } = new();
    public string PotdTitle { get; set; } = "";
    public string PotdUrl { get; set; } = "";
    public DateOnly? PotdDate { get; set; }
    public int ActivityStreak { get; set; }
    public DateTimeOffset? LinkUpdatedAt { get; set; }
    public string LinkStatus { get; set; } = "";

    public DesktopWidget Duplicate()
    {
        var copy = (DesktopWidget)MemberwiseClone();
        copy.PotdCompletions = new(PotdCompletions);
        copy.Id = Guid.NewGuid();
        if (!string.IsNullOrWhiteSpace(copy.Title)) copy.Title += " copy";
        copy.X += 24; copy.Y += 24;
        return copy;
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsLeetCode => TryGetLink(Content, out var link)
        && (link!.Host.Equals("leetcode.com", StringComparison.OrdinalIgnoreCase) || link.Host.Equals("www.leetcode.com", StringComparison.OrdinalIgnoreCase));
    public int PotdStreak(DateOnly today)
    {
        var completed = PotdCompletions.ToHashSet();
        var day = completed.Contains(today) ? today : today.AddDays(-1);
        var streak = 0;
        while (completed.Contains(day)) { streak++; day = day.AddDays(-1); }
        return streak;
    }

    public static bool TryGetLink(string text, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)) return false;
        uri = parsed;
        return true;
    }

    public string CountdownText(DateTimeOffset now)
    {
        var remaining = Target - now;
        if (remaining <= TimeSpan.Zero) return "It’s time";
        return remaining.TotalDays >= 1
            ? $"{(int)remaining.TotalDays}d {remaining.Hours:00}h {remaining.Minutes:00}m"
            : $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }
}

internal sealed class DesktopCanvasDocument
{
    public bool Enabled { get; set; }
    public List<DesktopWidget> Widgets { get; set; } = new();
}

internal sealed class DesktopCanvasStore
{
    public string FilePath { get; }
    public DesktopCanvasStore(string path) => FilePath = Path.GetFullPath(path);

    public DesktopCanvasDocument Load()
    {
        if (!File.Exists(FilePath)) return new DesktopCanvasDocument();
        using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var document = JsonSerializer.Deserialize<DesktopCanvasDocument>(stream, JsonOptions.Shared)
            ?? throw new InvalidDataException("The desktop canvas could not be read.");
        stream.Position = 0;
        using var json = JsonDocument.Parse(stream);
        document.Widgets ??= new();
        if (TryGetSavedBoards(json.RootElement, out var boards))
        {
            var existing = document.Widgets.Where(widget => widget != null).Select(widget => widget.Id).ToHashSet();
            foreach (var board in boards.EnumerateArray())
            {
                if (!board.TryGetProperty("Widgets", out var saved) || saved.ValueKind != JsonValueKind.Array) continue;
                foreach (var widget in saved.Deserialize<List<DesktopWidget>>(JsonOptions.Shared) ?? new())
                {
                    if (widget == null) throw new InvalidDataException("The canvas contains an invalid widget.");
                    if (!existing.Add(widget.Id)) continue;
                    widget.Enabled = false;
                    document.Widgets.Add(widget);
                }
            }
        }
        var seen = new HashSet<Guid>();
        foreach (var widget in document.Widgets)
        {
            if (widget == null || !seen.Add(widget.Id)) throw new InvalidDataException("The canvas contains an invalid widget.");
            if (!Enum.IsDefined(widget.Kind)) throw new InvalidDataException("Unknown widget type.");
            widget.Title ??= "";
            widget.Content ??= "";
            widget.Width = Clamp(widget.Width, 120, 1600, 300);
            widget.Height = Clamp(widget.Height, 80, 1200, 230);
            widget.X = Clamp(widget.X, -100000, 100000, 160);
            widget.Y = Clamp(widget.Y, -100000, 100000, 100);
            widget.FontFamily = string.IsNullOrWhiteSpace(widget.FontFamily) ? "Segoe UI" : widget.FontFamily;
            widget.FontSize = Clamp(widget.FontSize, 10, 96, 16);
            widget.Opacity = Clamp(widget.Opacity, .2, 1, 1);
            widget.CornerRadius = Clamp(widget.CornerRadius, 0, 60, 12);
            widget.TextColor ??= "#F1F2EE";
            widget.BackgroundColor ??= "#1D201E";
            widget.BorderColor ??= "#738579";
            widget.BorderWidth = Clamp(widget.BorderWidth, 0, 6, 1);
            widget.Drawing ??= ""; widget.LeetCodeUsername ??= ""; widget.PotdCompletions ??= new();
            widget.LinkDescription ??= ""; widget.LinkCustomDescription ??= ""; widget.LinkPageTitle ??= "";
            widget.LinkStatus ??= ""; widget.PotdTitle ??= ""; widget.PotdUrl ??= "";
            widget.DrawingWidth = Clamp(widget.DrawingWidth, 1, 1600, 300);
            widget.DrawingHeight = Clamp(widget.DrawingHeight, 1, 1200, 180);
            if (widget.Drawing.Length > 6000000) throw new InvalidDataException("The drawing is too large.");
            if (!Enum.IsDefined(widget.Alignment)) widget.Alignment = WidgetTextAlignment.Left;
            if (!Enum.IsDefined(widget.ImageFit)) widget.ImageFit = WidgetImageFit.Fit;
        }
        return document;
    }

    private static double Clamp(double value, double min, double max, double fallback)
        => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    private static bool TryGetSavedBoards(JsonElement root, out JsonElement boards)
        => (root.TryGetProperty("Boards", out boards) || root.TryGetProperty("boards", out boards)) && boards.ValueKind == JsonValueKind.Array;

    public void Save(DesktopCanvasDocument document)
        => WithWriteLock(() => SaveCore(document));

    public void Update(Action<DesktopCanvasDocument> update)
        => WithWriteLock(() => { var document = Load(); update(document); SaveCore(document); });

    private void WithWriteLock(Action action)
    {
        using var mutex = new Mutex(false, WallpaperProfiles.Engine.DesktopCanvasProcess.MutexName(FilePath) + ".Store");
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("The canvas is busy. Try saving again.");
            action();
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }

    private void SaveCore(DesktopCanvasDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        if (File.Exists(FilePath))
        {
            using var previous = JsonDocument.Parse(File.ReadAllText(FilePath));
            if (TryGetSavedBoards(previous.RootElement, out _))
            {
                var backup = FilePath + ".boards-backup.json";
                if (File.Exists(backup)) backup = FilePath + ".boards-backup-" + Guid.NewGuid().ToString("N") + ".json";
                File.Copy(FilePath, backup);
            }
        }
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(document, JsonOptions.Shared));
            File.Move(temporary, FilePath, overwrite: true);
            if (EventWaitHandle.TryOpenExisting(WallpaperProfiles.Engine.DesktopCanvasProcess.MutexName(FilePath) + ".Changed", out var changed))
            { using (changed) changed.Set(); }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public bool UpdateLayout(Guid id, double x, double y, double width, double height)
    {
        var found = false;
        Update(document =>
        {
            var widget = document.Widgets.FirstOrDefault(w => w.Id == id);
            if (widget == null) return;
            widget.X = x; widget.Y = y; widget.Width = width; widget.Height = height;
            found = true;
        });
        return found;
    }
    public void UpdateWidget(Guid id, Action<DesktopWidget> update)
        => Update(document => { var widget = document.Widgets.FirstOrDefault(item => item.Id == id); if (widget != null) update(widget); });
}
