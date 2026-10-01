using System.Text.Json;

namespace WallpaperProfiles.Persistence;

internal enum DesktopWidgetKind { Note, Countdown, Image, Link, Sketch }
internal enum WidgetTextAlignment { Left, Center, Right }
internal enum WidgetImageFit { Fit, Fill, Stretch }
internal enum BoardWallpaperMode { Normal, Timed, Rotating, RotatingTimed }

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
    public List<DesktopBoard> Boards { get; set; } = new();
    public Guid? ActiveBoardId { get; set; }
    public bool AutoSwitchBoards { get; set; }
    public DateTime? ManualBoardUntil { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public DesktopBoard? ActiveBoard => Boards.FirstOrDefault(board => board.Id == ActiveBoardId);
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<DesktopWidget> AllWidgets => Boards.Count == 0 ? Widgets : Boards.SelectMany(board => board.Widgets);
    public void InitializeBoards()
    {
        Boards ??= new(); Widgets ??= new();
        if (Boards.Count == 0) Boards.Add(new DesktopBoard { Id = Guid.Empty, Name = "Chill", Widgets = Widgets });
        foreach (var board in Boards)
        {
            if (board == null) throw new InvalidDataException("The canvas contains an invalid board.");
            board.Widgets ??= new(); board.Name ??= "New board"; board.WallpaperPath ??= "";
            board.OverrideWallpaperPath ??= "";
            if (board.WallpaperMode.HasValue && !Enum.IsDefined(board.WallpaperMode.Value)) board.WallpaperMode = null;
            if (board.RotationMinutes.HasValue) board.RotationMinutes = Math.Clamp(board.RotationMinutes.Value, 1, 1440);
        }
        var active = ActiveBoard ?? Boards[0];
        ActiveBoardId = active.Id; Widgets = active.Widgets;
    }
    public void SwitchBoard(Guid id, DateTime now, bool manual = true)
    {
        var board = Boards.FirstOrDefault(item => item.Id == id) ?? throw new InvalidOperationException("This board was removed.");
        ActiveBoardId = board.Id; Widgets = board.Widgets;
        ManualBoardUntil = manual && AutoSwitchBoards ? NextBoardBoundary(now) : null;
    }
    public DateTime? NextBoardBoundary(DateTime now)
        => Boards.Where(board => board.SwitchAt.HasValue).Select(board =>
        {
            var next = now.Date.Add(board.SwitchAt!.Value.ToTimeSpan());
            return next <= now ? next.AddDays(1) : next;
        }).OrderBy(next => next).Cast<DateTime?>().FirstOrDefault();
    public bool ApplyBoardSchedule(DateTime now)
    {
        if (!AutoSwitchBoards || ManualBoardUntil > now) return false;
        var scheduled = Boards.Where(board => board.SwitchAt.HasValue).OrderBy(board => board.SwitchAt).ToArray();
        if (scheduled.Length == 0) return false;
        var board = scheduled.LastOrDefault(item => item.SwitchAt!.Value <= TimeOnly.FromDateTime(now)) ?? scheduled[^1];
        if (ActiveBoardId == board.Id) return false;
        SwitchBoard(board.Id, now, manual: false);
        return true;
    }
}

internal sealed class DesktopBoard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New board";
    public string WallpaperPath { get; set; } = "";
    public WallpaperProfiles.Models.FitMode WallpaperFit { get; set; } = WallpaperProfiles.Models.FitMode.Fill;
    public WallpaperProfiles.Models.WallpaperProfile? Scene { get; set; }
    public BoardWallpaperMode? WallpaperMode { get; set; }
    public int? RotationMinutes { get; set; }
    public bool? RotationRandom { get; set; }
    public string OverrideWallpaperPath { get; set; } = "";
    public TimeOnly OverrideStart { get; set; } = new(20, 0);
    public TimeOnly OverrideEnd { get; set; } = new(8, 0);
    [System.Text.Json.Serialization.JsonIgnore]
    public BoardWallpaperMode EffectiveWallpaperMode => WallpaperMode ?? (Scene?.SlideshowIntervalMinutes > 0 ? BoardWallpaperMode.Rotating : BoardWallpaperMode.Normal);
    public bool IsWallpaperOverrideActive(DateTime now)
    {
        if (EffectiveWallpaperMode is not (BoardWallpaperMode.Timed or BoardWallpaperMode.RotatingTimed) || OverrideStart == OverrideEnd || string.IsNullOrWhiteSpace(OverrideWallpaperPath)) return false;
        var time = TimeOnly.FromDateTime(now);
        return OverrideStart < OverrideEnd ? time >= OverrideStart && time < OverrideEnd : time >= OverrideStart || time < OverrideEnd;
    }
    public TimeOnly? SwitchAt { get; set; }
    public List<DesktopWidget> Widgets { get; set; } = new();
}

internal sealed class DesktopCanvasStore
{
    public string FilePath { get; }
    public DesktopCanvasStore(string path) => FilePath = Path.GetFullPath(path);

    public DesktopCanvasDocument Load()
    {
        if (!File.Exists(FilePath)) { var empty = new DesktopCanvasDocument(); empty.InitializeBoards(); return empty; }
        using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var document = JsonSerializer.Deserialize<DesktopCanvasDocument>(stream, JsonOptions.Shared)
            ?? throw new InvalidDataException("The desktop canvas could not be read.");
        document.InitializeBoards();
        if (document.Boards.Select(board => board.Id).Distinct().Count() != document.Boards.Count)
            throw new InvalidDataException("The canvas contains duplicate boards.");
        var seen = new HashSet<Guid>();
        foreach (var widget in document.AllWidgets)
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
        document.InitializeBoards();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
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
            var widget = document.AllWidgets.FirstOrDefault(w => w.Id == id);
            if (widget == null) return;
            widget.X = x; widget.Y = y; widget.Width = width; widget.Height = height;
            found = true;
        });
        return found;
    }
    public void UpdateWidget(Guid id, Action<DesktopWidget> update)
        => Update(document => { var widget = document.AllWidgets.FirstOrDefault(item => item.Id == id); if (widget != null) update(widget); });
}
