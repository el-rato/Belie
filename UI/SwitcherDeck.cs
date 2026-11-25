using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace WallpaperProfiles.UI;

/// <summary>How the cards of the wallpaper switcher look.</summary>
internal sealed record SwitcherLook(SwitcherLayout Layout, double Size, double Slant, double Spacing, double Radius, double Outline, int Sides, double Speed)
{
    public static SwitcherLook From(AppSettings settings) => new(settings.SwitcherLayout, settings.SwitcherSize, settings.SwitcherSlant,
        settings.SwitcherSpacing, settings.SwitcherRadius, settings.SwitcherOutline, settings.SwitcherSides, settings.SwitcherSpeed);
}

/// <summary>Where a card goes, around the middle of the deck.</summary>
internal readonly record struct CardPlace(double X, double Y, double Scale, double Opacity, int Order);

/// <summary>A picture cut into a leaning card. The picture itself stays upright.</summary>
internal sealed class SwitcherCard : FrameworkElement
{
    private static readonly System.Windows.Media.Brush Empty = Frozen(Color.FromArgb(150, 22, 24, 28));
    private ImageSource? _image;
    private SwitcherLook? _look;
    private System.Windows.Media.Brush _outline = Brushes.Black;

    public string Path { get; init; } = "";
    public int Index { get; init; }
    public bool HasImage => _image != null;
    public ScaleTransform Scaling { get; } = new(1, 1);
    public TranslateTransform Moving { get; } = new();

    public SwitcherCard()
    {
        RenderTransformOrigin = new Point(.5, .5);
        RenderTransform = new TransformGroup { Children = { Scaling, Moving } };
        Cursor = System.Windows.Input.Cursors.Hand;
    }

    public void Show(ImageSource image) { _image = image; InvalidateVisual(); }

    public void Shape(SwitcherLook look, System.Windows.Media.Brush outline)
    {
        if (_look == look && _outline == outline) return;
        _look = look; _outline = outline;
        InvalidateVisual();
    }

    /// <summary>How far the top of a card reaches past its bottom.</summary>
    public static double Lean(double height, double slant) => height * Math.Tan(slant * Math.PI / 180);

    public static Geometry Outline(Size size, double slant, double radius)
    {
        var lean = Lean(size.Height, slant);
        double left = Math.Max(0, -lean), right = Math.Max(0, lean);
        var corners = new[]
        {
            new Point(right, 0), new Point(size.Width - left, 0), new Point(size.Width - right, size.Height), new Point(left, size.Height)
        };
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var i = 0; i < 4; i++)
            {
                var corner = corners[i];
                Vector before = corners[(i + 3) % 4] - corner, after = corners[(i + 1) % 4] - corner;
                var cut = Math.Min(radius, Math.Min(before.Length, after.Length) / 2);
                before.Normalize(); after.Normalize();
                if (i == 0) context.BeginFigure(corner + before * cut, true, true); else context.LineTo(corner + before * cut, true, true);
                if (cut > 0) context.QuadraticBezierTo(corner, corner + after * cut, true, true);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    protected override void OnRender(DrawingContext context)
    {
        if (_look == null || RenderSize.Width < 2 || RenderSize.Height < 2) return;
        var outline = Outline(RenderSize, _look.Slant, _look.Radius);
        context.DrawGeometry(Empty, null, outline);
        if (_image != null)
        {
            var scale = Math.Max(RenderSize.Width / _image.Width, RenderSize.Height / _image.Height);
            double width = _image.Width * scale, height = _image.Height * scale;
            context.PushClip(outline);
            context.DrawImage(_image, new Rect((RenderSize.Width - width) / 2, (RenderSize.Height - height) / 2, width, height));
            context.Pop();
        }
        if (_look.Outline > 0) context.DrawGeometry(null, new Pen(_outline, _look.Outline) { LineJoin = PenLineJoin.Round }, outline);
    }

    private static System.Windows.Media.Brush Frozen(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this);
}

/// <summary>
/// The wallpapers of a profile as cards to move through. Only the cards near the chosen one exist, so a folder can
/// hold any number of pictures.
/// </summary>
internal sealed class SwitcherDeck : Canvas
{
    private const int Kept = 48;
    private readonly Dictionary<int, SwitcherCard> _cards = new();
    private readonly Dictionary<string, ImageSource> _pictures = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _recent = new();
    private readonly HashSet<string> _loading = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _reading = new(3);
    private readonly Stopwatch _clock = new();
    private IReadOnlyList<string> _items = Array.Empty<string>();
    private SwitcherLook _look = SwitcherLook.From(new AppSettings());
    private System.Windows.Media.Brush _outline = Brushes.Black;
    private double _position, _target;
    private bool _moving, _closed;

    /// <summary>The proportions of the screen the pictures are for.</summary>
    public double Aspect { get; set; } = 16 / 9d;
    /// <summary>The height that the size is a share of, when the deck covers only part of the screen.</summary>
    public double ScreenHeight { get; set; }
    public Func<string, int, ImageSource?> Read { get; init; } = ReadPicture;
    public event Action<string>? Chosen;
    public event Action? SelectionChanged;
    /// <summary>A picture that may be the selected one has been read.</summary>
    public event Action<string>? PictureRead;

    public SwitcherDeck()
    {
        ClipToBounds = false;
        Background = Brushes.Transparent;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        SizeChanged += (_, _) => Arrange();
        MouseWheel += (_, e) => { MoveBy(e.Delta > 0 ? -1 : 1); e.Handled = true; };
        Unloaded += (_, _) => StopMoving();
    }

    public int Selected => _items.Count == 0 ? -1 : Wrap((int)Math.Round(_target));
    public string SelectedPath => Selected < 0 ? "" : _items[Selected];
    public IReadOnlyList<string> Items => _items;
    internal IReadOnlyDictionary<int, SwitcherCard> Cards => _cards;
    internal bool IsMoving => _moving;
    internal ImageSource? Picture(string path) => _pictures.GetValueOrDefault(path);
    /// <summary>Goes around, when there are enough cards that none would be seen twice.</summary>
    private bool Endless => _look.Layout != SwitcherLayout.Grid && _items.Count >= _look.Sides * 2 + 3;
    private bool Animates => _look.Speed > 0 && SystemParameters.ClientAreaAnimation;

    public void Show(IReadOnlyList<string> items, string selected)
    {
        _items = items;
        foreach (var card in _cards.Values) Children.Remove(card);
        _cards.Clear();
        var index = 0;
        for (var i = 0; i < items.Count; i++) if (string.Equals(items[i], selected, StringComparison.OrdinalIgnoreCase)) index = i;
        _position = _target = index;
        Arrange();
        SelectionChanged?.Invoke();
    }

    public void Apply(SwitcherLook look, System.Windows.Media.Brush outline)
    {
        (_look, _outline) = (look, outline);
        // A target that went around may be out of range for a deck that does not.
        if (_items.Count > 0) _position = _target = Selected;
        Arrange();
    }

    public void MoveBy(int by)
    {
        if (_items.Count == 0 || by == 0) return;
        var target = Math.Round(_target) + by;
        // Past the end of a short deck is its other end.
        if (!Endless) target = Math.Abs(by) == 1 ? Wrap((int)target) : Math.Clamp(target, 0, _items.Count - 1);
        MoveTo(target);
    }

    /// <summary>Up and down, where there are rows.</summary>
    public void MoveRows(int by) => MoveBy(_look.Layout == SwitcherLayout.Grid ? by * Columns : by);

    public void Select(int index)
    {
        if (_items.Count == 0) return;
        index = Math.Clamp(index, 0, _items.Count - 1);
        MoveTo(Endless ? Math.Round(_target) + Distance(index, Math.Round(_target)) : index);
    }

    public void Choose() { if (Selected >= 0) Chosen?.Invoke(SelectedPath); }

    private void MoveTo(double target)
    {
        if (target == _target) return;
        _target = target;
        if (Animates)
        {
            if (!_moving) { _moving = true; _clock.Restart(); CompositionTarget.Rendering += Move; }
        }
        else { _position = _target; Arrange(); }
        SelectionChanged?.Invoke();
    }

    private void Move(object? sender, EventArgs e)
    {
        var seconds = Math.Min(.05, _clock.Elapsed.TotalSeconds);
        _clock.Restart();
        _position += (_target - _position) * (1 - Math.Exp(-seconds * 16 * _look.Speed));
        if (Math.Abs(_target - _position) < .002) { _position = _target; StopMoving(); }
        Arrange();
    }

    private void StopMoving() { if (!_moving) return; _moving = false; CompositionTarget.Rendering -= Move; }

    private int Wrap(int index) => (index % _items.Count + _items.Count) % _items.Count;

    /// <summary>From one position to an item, the short way around.</summary>
    private double Distance(int index, double from)
    {
        var distance = index - from;
        if (!Endless) return distance;
        var count = _items.Count;
        return distance - count * Math.Round(distance / count);
    }

    private double FullHeight => ScreenHeight > 0 ? ScreenHeight : ActualHeight;

    /// <summary>The chosen card's size, with the part that leans.</summary>
    public Size CardSize
    {
        get
        {
            var height = Math.Max(20, FullHeight * _look.Size);
            var width = height * Aspect;
            if (_look.Layout == SwitcherLayout.Grid)
            {
                var room = (ActualWidth - _look.Spacing * (Columns + 1)) / Columns;
                width = Math.Max(20, room); height = width / Aspect;
                return new Size(width, height);
            }
            return new Size(width + Math.Abs(SwitcherCard.Lean(height, _look.Slant)), height);
        }
    }

    private int Columns => Math.Max(1, (int)Math.Round(ActualWidth / Math.Max(40, FullHeight * _look.Size * .75 * Aspect + _look.Spacing)));

    /// <summary>Where the card that is so many steps from the chosen one goes.</summary>
    internal static CardPlace Place(SwitcherLook look, double steps, Size card)
    {
        double away = Math.Abs(steps), side = Math.Sign(steps), sides = look.Sides;
        // Cards past the last side fade away while they arrive or leave.
        var edge = Math.Clamp(sides + 1 - away, 0, 1);
        var body = card.Width - Math.Abs(SwitcherCard.Lean(card.Height, look.Slant));
        var order = 1000 - (int)Math.Round(away * 10);
        switch (look.Layout)
        {
            case SwitcherLayout.Carousel:
            {
                var scale = Math.Max(.3, 1 - .22 * away);
                var x = side * (body * .62 * away * (1 - .06 * away) + look.Spacing * away);
                return new CardPlace(x, card.Height * .07 * away * away, scale, edge * Math.Max(.2, 1 - .18 * away), order);
            }
            case SwitcherLayout.Filmstrip:
            {
                var scale = .62 + .12 * Math.Max(0, 1 - away);
                return new CardPlace(steps * (body * .62 + look.Spacing), 0, scale, edge * (away < 1 ? 1 - .25 * away : .75), order);
            }
            default:
            {
                // The chosen card is nearly twice the size of the ones it overlaps.
                var scale = away < 1 ? 1 - .45 * away : Math.Max(.2, .55 - .06 * (away - 1));
                var first = body * .555 + look.Spacing;
                var x = away < 1 ? first * away : first + (away - 1) * (body * .445 + look.Spacing);
                var opacity = away <= 1 ? 1 - .1 * away : Math.Max(.12, .9 - .42 * (away - 1));
                return new CardPlace(side * x, 0, scale, edge * opacity, order);
            }
        }
    }

    private void Arrange()
    {
        if (_closed || ActualWidth < 2 || ActualHeight < 2) return;
        var size = CardSize;
        var grid = _look.Layout == SwitcherLayout.Grid;
        var wanted = new HashSet<int>();
        if (grid)
        {
            // The row of the chosen card stays in view; its neighbors scroll past.
            var rowHeight = size.Height + _look.Spacing;
            var rows = (_items.Count + Columns - 1) / Columns;
            var row = _position / Columns;
            var scroll = Math.Clamp(Math.Floor(row) * rowHeight + rowHeight / 2 - ActualHeight / 2, 0, Math.Max(0, rows * rowHeight + _look.Spacing - ActualHeight));
            var top = rows * rowHeight + _look.Spacing < ActualHeight ? (ActualHeight - rows * rowHeight + _look.Spacing) / 2 : _look.Spacing;
            for (var i = 0; i < _items.Count; i++)
            {
                var y = top + i / Columns * rowHeight - scroll;
                if (y > ActualHeight || y + size.Height < 0) continue;
                wanted.Add(i);
                var chosen = i == Selected;
                Put(i, size, _look.Spacing + i % Columns * (size.Width + _look.Spacing), y, new CardPlace(0, 0, chosen ? 1 : .94, chosen ? 1 : .72, chosen ? 1000 : 1));
            }
        }
        else
        {
            var reach = _look.Sides + 1;
            for (var i = 0; i < _items.Count; i++)
            {
                var steps = Distance(i, _position);
                if (Math.Abs(steps) >= reach) continue;
                wanted.Add(i);
                Put(i, size, (ActualWidth - size.Width) / 2, (ActualHeight - size.Height) / 2, Place(_look, steps, size));
            }
        }
        foreach (var index in _cards.Keys.Where(index => !wanted.Contains(index)).ToArray())
        {
            Children.Remove(_cards[index]);
            _cards.Remove(index);
        }
    }

    private void Put(int index, Size size, double left, double top, CardPlace place)
    {
        if (!_cards.TryGetValue(index, out var card))
        {
            card = new SwitcherCard { Index = index, Path = _items[index] };
            AutomationProperties.SetName(card, System.IO.Path.GetFileNameWithoutExtension(card.Path));
            card.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (card.Index == Selected) Choose(); else Select(card.Index);
            };
            _cards[index] = card;
            Children.Add(card);
            if (_pictures.TryGetValue(card.Path, out var known)) { card.Show(known); Touch(card.Path); }
        }
        card.Width = size.Width; card.Height = size.Height;
        SetLeft(card, left); SetTop(card, top);
        SetZIndex(card, place.Order);
        card.Shape(_look, _outline);
        card.Scaling.ScaleX = card.Scaling.ScaleY = place.Scale;
        card.Moving.X = place.X; card.Moving.Y = place.Y;
        card.Opacity = place.Opacity;
        AutomationProperties.SetItemStatus(card, index == Selected ? "Selected" : "");
        if (!card.HasImage) Load(card.Path);
    }

    private async void Load(string path)
    {
        if (!_loading.Add(path)) return;
        var height = (int)Math.Ceiling(Math.Max(120, CardSize.Height) * VisualTreeHelper.GetDpi(this).DpiScaleY);
        await _reading.WaitAsync();
        try
        {
            // It may have scrolled out of sight while others were read.
            if (_closed || !_cards.Values.Any(card => card.Path == path)) return;
            var picture = await Task.Run(() => Read(path, height));
            if (_closed || picture == null) return;
            _pictures[path] = picture;
            Touch(path);
            foreach (var card in _cards.Values.Where(card => card.Path == path)) card.Show(picture);
            PictureRead?.Invoke(path);
        }
        catch (Exception ex) { Logger.Warn("A wallpaper could not be shown in the switcher: " + ex.Message); }
        finally { _reading.Release(); _loading.Remove(path); }
    }

    private void Touch(string path)
    {
        _recent.Remove(path);
        _recent.Add(path);
        while (_recent.Count > Kept) { _pictures.Remove(_recent[0]); _recent.RemoveAt(0); }
    }

    /// <summary>Lets go of the pictures.</summary>
    public void Close()
    {
        _closed = true;
        StopMoving();
        Children.Clear();
        _cards.Clear(); _pictures.Clear(); _recent.Clear();
    }

    private static ImageSource? ReadPicture(string path, int height)
    {
        if (WallpaperEngine.IsVideoFile(path)) return VideoFrameGrabber.GrabFrame(path, height * 16 / 9);
        using var stream = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.DecodePixelHeight = height;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
