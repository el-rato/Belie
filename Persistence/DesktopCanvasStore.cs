using System.Text.Json;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Persistence;

internal sealed class DesktopCanvasStore
{
    public string FilePath { get; }
    public DesktopCanvasStore(string path) => FilePath = Path.GetFullPath(path);

    public DesktopCanvasDocument Load() => AtomicJsonFile.WithLock(FilePath, LoadCore);

    private DesktopCanvasDocument LoadCore()
    {
        if (!File.Exists(FilePath)) return new DesktopCanvasDocument();
        using var stream = AtomicJsonFile.OpenRead(FilePath);
        using var json = JsonSerializer.Deserialize<JsonDocument>(stream, JsonOptions.Shared)
            ?? throw new InvalidDataException("The desktop canvas could not be read.");
        var document = json.RootElement.Deserialize<DesktopCanvasDocument>(JsonOptions.Shared)
            ?? throw new InvalidDataException("The desktop canvas could not be read.");
        document.Widgets ??= new();
        document.ActiveWallpaperPath ??= "";
        document.WallpaperLayouts ??= new(); document.LayoutPresets ??= new(); document.StylePresets ??= new();
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
            NormalizeWidget(widget);
        }
        foreach (var layout in document.WallpaperLayouts)
            if (layout == null || string.IsNullOrWhiteSpace(layout.WallpaperPath)) throw new InvalidDataException("The canvas contains an invalid wallpaper layout.");
        foreach (var preset in document.LayoutPresets)
        {
            if (preset == null) throw new InvalidDataException("The canvas contains an invalid layout preset.");
            preset.Name ??= "Untitled layout"; preset.Widgets ??= new();
            foreach (var widget in preset.Widgets) NormalizeWidget(widget);
        }
        foreach (var preset in document.StylePresets)
        {
            if (preset == null) throw new InvalidDataException("The canvas contains an invalid skin.");
            preset.Name ??= "Untitled skin"; NormalizeWidget(preset.Style);
        }
        return document;
    }

    internal static void NormalizeWidget(DesktopWidget widget)
    {
        if (widget == null) throw new InvalidDataException("The canvas contains an invalid widget.");
        if (!Enum.IsDefined(widget.Kind)) throw new InvalidDataException("Unknown widget type.");
        widget.Title ??= "";
        widget.Content ??= "";
        widget.WallpaperPath ??= "";
        if (!Enum.IsDefined(widget.ClockSkin)) widget.ClockSkin = ClockSkin.Script;
        if (!Enum.IsDefined(widget.SpectrumSkin)) widget.SpectrumSkin = SpectrumSkin.Mirrored;
        widget.SpectrumDensity = Math.Clamp(widget.SpectrumDensity, 12, 96);
        widget.SpectrumThickness = Clamp(widget.SpectrumThickness, 1, 10, 2);
        widget.SpectrumSensitivity = Clamp(widget.SpectrumSensitivity, .25, 3, 1);
        widget.SpectrumSmoothing = Clamp(widget.SpectrumSmoothing, 0, .95, .65);
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
        widget.MusicAccentColor ??= "#A9C9B4";
        if (!Enum.IsDefined(widget.MusicLayout)) widget.MusicLayout = NowPlayingLayout.Horizontal;
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
            using var stream = AtomicJsonFile.OpenRead(FilePath);
            using var previous = JsonDocument.Parse(stream);
            if (TryGetSavedBoards(previous.RootElement, out _))
            {
                var backup = FilePath + ".boards-backup.json";
                if (File.Exists(backup)) backup = FilePath + ".boards-backup-" + Guid.NewGuid().ToString("N") + ".json";
                File.Copy(FilePath, backup);
            }
        }
        AtomicJsonFile.Write(FilePath, document);
        if (EventWaitHandle.TryOpenExisting(WallpaperProfiles.Engine.DesktopCanvasProcess.MutexName(FilePath) + ".Changed", out var changed))
        { using (changed) changed.Set(); }
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
