using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using Bitmap = System.Drawing.Bitmap;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;

namespace WallpaperProfiles.UI;

internal sealed class TransitionsPage : SettingsPage<AppSettings>
{
    internal static readonly (WallpaperTransitionStyle Style, string Name, string About)[] Styles =
    {
        (WallpaperTransitionStyle.Automatic, "Automatic", "Outlines the subject when the picture has a clear one, otherwise spreads in colonies."),
        (WallpaperTransitionStyle.Colonies, "Pixel colonies", "Patches land on the screen and eat through it."),
        (WallpaperTransitionStyle.Grow, "Grow", "A circle opens from a random point."),
        (WallpaperTransitionStyle.Shrink, "Shrink", "Closes in on a random point."),
        (WallpaperTransitionStyle.Wipe, "Wipe", "A straight edge crosses the screen."),
        (WallpaperTransitionStyle.Wave, "Wave", "A rippling edge crosses the screen."),
        (WallpaperTransitionStyle.Blinds, "Blinds", "Strips open side by side."),
        (WallpaperTransitionStyle.Spiral, "Spiral", "Sweeps around the middle."),
        (WallpaperTransitionStyle.Dissolve, "Dissolve", "Single squares change here and there."),
        (WallpaperTransitionStyle.Fade, "Fade", "The whole picture blends into the next."),
        (WallpaperTransitionStyle.Random, "Surprise me", "A different one each time."),
        (WallpaperTransitionStyle.None, "None", "The picture changes at once.")
    };

    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly TransitionPreview _preview;
    private readonly Dictionary<WallpaperTransitionStyle, Button> _buttons = new();
    private WallpaperTransitionStyle _style;
    private TextBlock? _about;
    private Func<TransitionOptions>? _options;
    private readonly Func<IEnumerable<string>> _wallpapers;
    private static CancellationTokenSource? _downloading;

    /// <param name="pictures">Two pictures to show in the preview; fewer are made up.</param>
    /// <param name="wallpapers">Every wallpaper, to cut out once the model arrives.</param>
    public TransitionsPage(SettingsStore store, AppSettings settings, Func<IEnumerable<string>> pictures, Func<IEnumerable<string>>? wallpapers = null)
        : base("Transitions", "How one wallpaper gives way to the next, in slideshows and when profiles change.")
    {
        _store = store; _settings = settings;
        _wallpapers = wallpapers ?? Array.Empty<string>;
        _preview = new TransitionPreview(pictures);
        Reload();
    }

    protected override AppSettings Load() => _settings;
    protected override void Store(AppSettings settings) => _store.Save(settings);

    protected override void Build(AppSettings settings)
    {
        _buttons.Clear();
        _style = settings.WallpaperTransition;
        if (_preview.Parent is System.Windows.Controls.Panel before) before.Children.Remove(_preview);
        Tab("Style");
        Add(_preview);
        var styles = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var item in Styles)
        {
            var button = Press(item.Name, "Transition" + item.Style, () => Choose(item.Style));
            button.ToolTip = item.About;
            _buttons[item.Style] = button; styles.Children.Add(button);
        }
        Add(styles);
        _about = Hint("");
        OnSave(to => to.WallpaperTransition = _style);
        BuildModel();

        Tab("Options");
        var seconds = Slide("Length", "TransitionSeconds", .3, 6, .1, settings.TransitionSeconds, " s", (to, value) => to.TransitionSeconds = Math.Round(value, 1));
        var cell = Slide("Square size", "TransitionCell", 2, 48, 1, settings.TransitionCell, " px", (to, value) => to.TransitionCell = (int)Math.Round(value));
        Hint("Small squares look smooth, large ones look like pixels. A fade has none.");
        var angle = Slide("Direction", "TransitionAngle", 0, 345, 15, settings.TransitionAngle, "°", (to, value) => to.TransitionAngle = value);
        Hint("For Wipe, Wave and Blinds. 0° goes to the right, 90° goes down.");
        var dim = Slide("Dim the old wallpaper", "TransitionDim", 0, 90, 5, Math.Round(settings.TransitionDim * 100), "%", (to, value) => to.TransitionDim = value / 100);
        Hint("While the subject's outline is drawn, so that the line stands out. 0% leaves it as it is.");
        Heading("Glow");
        var glow = Switch("Squares glow as they change", "TransitionGlow", settings.TransitionGlow, (to, value) => to.TransitionGlow = value);
        var length = Slide("Glow length", "TransitionGlowLength", 1, 40, 1, Math.Round(settings.TransitionGlowLength * 100), "%", (to, value) => to.TransitionGlowLength = value / 100);
        var color = Color("Glow color", "TransitionGlowColor", settings.TransitionGlowColor, (to, value) => to.TransitionGlowColor = value, optional: true);
        Hint("Leave the color empty to take it from each picture.");
        _options = () => new TransitionOptions(_style, seconds.Value, (int)cell.Value, glow.IsChecked == true, color.Text.Trim(), length.Value / 100, angle.Value, dim.Value / 100);
        _preview.Options = () => _options();
        ShowStyle();
    }

    /// <summary>The optional BiRefNet model that finds subjects on busy pictures too.</summary>
    private void BuildModel()
    {
        Heading("Finding the subject");
        var state = Hint("");
        var bar = new System.Windows.Controls.ProgressBar { Height = 4, Maximum = 1, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8), MaxWidth = 420, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        Add(bar);
        var buttons = new WrapPanel();
        Button? get = null, remove = null;
        void Show()
        {
            var pending = SubjectModel.Pending;
            state.Text = _downloading != null ? "Downloading the model…"
                : SubjectModel.IsInstalled ? "BiRefNet is installed. Each wallpaper is cut out once, in the background, the first time it is coming up; until then it gets the simple outline or colonies." + (pending > 0 ? $" {pending} waiting." : "")
                : $"Automatic only finds subjects on plain backgrounds. BiRefNet, a model that runs on this computer, finds them in busy pictures too. It is a {SubjectModel.ModelBytes / 1_000_000} MB download from rembg's releases on GitHub; nothing is sent anywhere.";
            get!.Visibility = SubjectModel.IsInstalled ? Visibility.Collapsed : Visibility.Visible;
            get.Content = _downloading != null ? "Cancel" : "Download BiRefNet";
            remove!.Visibility = SubjectModel.IsInstalled ? Visibility.Visible : Visibility.Collapsed;
        }
        get = Press("Download BiRefNet", "SubjectModelDownload", async () =>
        {
            if (_downloading != null) { _downloading.Cancel(); return; }
            using var cancel = _downloading = new CancellationTokenSource();
            bar.Visibility = Visibility.Visible; bar.Value = 0;
            Show();
            try
            {
                await SubjectModel.DownloadAsync(new Progress<double>(done => bar.Value = done), cancel.Token);
                SubjectModel.Warm(_wallpapers());
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Logger.Error("Downloading the subject model failed.", ex); Status = "The model could not be downloaded: " + ex.Message; }
            finally { _downloading = null; bar.Visibility = Visibility.Collapsed; Show(); }
        });
        remove = Press("Remove the model", "SubjectModelRemove", () =>
        {
            try { SubjectModel.Remove(); }
            catch (Exception ex) { Status = "The model could not be removed: " + ex.Message; }
            Show();
        });
        buttons.Children.Add(get); buttons.Children.Add(remove);
        Add(buttons);
        void Moved(int _) => Dispatcher.BeginInvoke(Show);
        SubjectModel.Progress += Moved;
        Unloaded += (_, _) => SubjectModel.Progress -= Moved;
        Show();
    }

    private void Choose(WallpaperTransitionStyle style)
    {
        _style = style;
        ShowStyle(); Changed();
        _preview.Play();
    }

    private void ShowStyle()
    {
        foreach (var (style, button) in _buttons)
        {
            if (style == _style) button.SetResourceReference(BackgroundProperty, "AccentSubtleBrush"); else button.ClearValue(BackgroundProperty);
            AutomationProperties.SetItemStatus(button, style == _style ? "Selected" : "");
        }
        if (_about != null) _about.Text = Styles.First(item => item.Style == _style).About;
    }
}

/// <summary>Plays the transition in the page, with the same code that plays it on the desktop.</summary>
internal sealed class TransitionPreview : Grid
{
    private const int Width_ = 640, Height_ = 360;
    private readonly Func<IEnumerable<string>> _paths;
    private readonly WriteableBitmap _shown = new(Width_, Height_, 96, 96, PixelFormats.Bgr32, null);
    private readonly Stopwatch _clock = new();
    private TransitionPainter? _painter;
    private Bitmap[]? _pictures;
    private (byte[], System.Drawing.RectangleF)?[] _cutouts = Array.Empty<(byte[], System.Drawing.RectangleF)?>();
    private int _current;
    private double _length;
    private Task<(Bitmap[], (byte[], System.Drawing.RectangleF)?[])>? _reading;

    public Func<TransitionOptions> Options { get; set; } = () => WallpaperTransitionStyle.Automatic;
    public bool IsPlaying => _clock.IsRunning;

    public TransitionPreview(Func<IEnumerable<string>> paths)
    {
        _paths = paths;
        MaxWidth = 560; HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        var image = new Image { Source = _shown, Stretch = Stretch.Uniform };
        AutomationProperties.SetName(image, "Transition preview");
        var play = new Button { Content = "Play", HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10), Padding = new Thickness(14, 7, 14, 7) };
        AutomationProperties.SetAutomationId(play, "TransitionPlay");
        play.Click += (_, _) => Play();
        Children.Add(image); Children.Add(play);
        IsVisibleChanged += (_, _) => { if (IsVisible) Show(); else Stop(); };
        Unloaded += (_, _) => Release();
    }

    private async void Show()
    {
        try { if (await Prepare()) Paint(_pictures![_current]); }
        catch (Exception ex) { Logger.Error("The transition preview failed.", ex); }
    }

    private async Task<bool> Prepare()
    {
        if (_pictures != null) return true;
        if (_reading == null) { var paths = _paths().ToArray(); _reading = Task.Run(() => Read(paths)); }
        // Showing and playing may both wait for the same pictures.
        var reading = _reading;
        try { await reading; }
        catch { if (_reading == reading) _reading = null; throw; }
        if (_reading == reading) { _reading = null; (_pictures, _cutouts) = reading.Result; }
        return _pictures != null;
    }

    private static (Bitmap[], (byte[], System.Drawing.RectangleF)?[]) Read(IEnumerable<string> paths)
    {
        var pictures = new List<Bitmap>();
        var cutouts = new List<(byte[], System.Drawing.RectangleF)?>();
        foreach (var path in paths)
        {
            if (pictures.Count == 2) break;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var image = System.Drawing.Image.FromStream(stream, false, false);
                var place = WallpaperTransition.Place(image.Size, FitMode.Fill, new System.Drawing.Rectangle(0, 0, Width_, Height_), new System.Drawing.Rectangle(0, 0, Width_, Height_));
                var picture = new Bitmap(Width_, Height_, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                using (var graphics = System.Drawing.Graphics.FromImage(picture)) { graphics.InterpolationMode = InterpolationMode.HighQualityBicubic; graphics.DrawImage(image, place); }
                pictures.Add(picture);
                cutouts.Add(SubjectModel.Cached(path) is { } map
                    ? (map, new System.Drawing.RectangleF(place.X / (float)Width_, place.Y / (float)Height_, place.Width / (float)Width_, place.Height / (float)Height_)) : null);
            }
            catch (Exception ex) { Logger.Warn("A picture could not be used for the transition preview: " + ex.Message); }
        }
        var colors = new[] { (0x1B2B3A, 0x79D5C8), (0x3A1B2B, 0xFFA58C) };
        while (pictures.Count < 2)
        {
            var (from, to) = colors[pictures.Count];
            var picture = new Bitmap(Width_, Height_, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            using (var graphics = System.Drawing.Graphics.FromImage(picture))
            using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(new System.Drawing.Rectangle(0, 0, Width_, Height_), System.Drawing.Color.FromArgb(255 << 24 | from), System.Drawing.Color.FromArgb(255 << 24 | to), 60f))
                graphics.FillRectangle(brush, 0, 0, Width_, Height_);
            pictures.Add(picture);
            cutouts.Add(null);
        }
        return (pictures.ToArray(), cutouts.ToArray());
    }

    private void Paint(Bitmap picture)
    {
        var data = picture.LockBits(new System.Drawing.Rectangle(0, 0, Width_, Height_), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        _shown.WritePixels(new Int32Rect(0, 0, Width_, Height_), data.Scan0, data.Stride * Height_, data.Stride);
        picture.UnlockBits(data);
    }

    internal async void Play()
    {
        try
        {
            if (!await Prepare() || _pictures == null) return;
            Stop();
            var options = Options();
            Paint(_pictures[_current]);
            _current = 1 - _current;
            var next = _pictures[_current];
            if (options.Style == WallpaperTransitionStyle.None) { Paint(next); return; }
            // The squares keep their proportion to the screen.
            options = options with { Style = TransitionField.Resolve(options.Style, Random.Shared), Cell = Math.Max(1, (int)Math.Round(options.Cell * Width_ / SystemParameters.PrimaryScreenWidth)) };
            var plan = WallpaperTransition.Plan(next, options, Random.Shared, _cutouts.ElementAtOrDefault(_current));
            _length = options.Seconds * (plan.Field?.Lines.Count > 0 ? 1100 : 1000);
            _painter = new TransitionPainter(_shown.BackBuffer, Width_, Height_);
            _painter.Begin(next, plan.Field, plan.Glow, plan.Line, plan.Options);
            _clock.Restart();
            CompositionTarget.Rendering += Frame;
        }
        catch (Exception ex) { Logger.Error("The transition preview failed.", ex); Stop(); }
    }

    private void Frame(object? sender, EventArgs e)
    {
        if (_painter == null) return;
        var progress = (float)Math.Min(1, _clock.Elapsed.TotalMilliseconds / _length);
        _shown.Lock();
        var dirty = _painter.Draw(progress);
        if (!dirty.IsEmpty) _shown.AddDirtyRect(new Int32Rect(dirty.X, dirty.Y, dirty.Width, dirty.Height));
        _shown.Unlock();
        if (progress >= 1) Stop();
    }

    private void Stop()
    {
        CompositionTarget.Rendering -= Frame;
        _clock.Reset();
        _painter?.Dispose(); _painter = null;
    }

    private void Release()
    {
        Stop();
        if (_pictures != null) foreach (var picture in _pictures) picture.Dispose();
        _pictures = null; _reading = null;
    }
}
