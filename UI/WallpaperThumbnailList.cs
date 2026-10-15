using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.ObjectModel;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Pen = System.Windows.Media.Pen;

namespace WallpaperProfiles.UI;

public sealed class WallpaperThumbnailList : ItemsControl
{
    private const string DragFormat = "Belie.ProfileWallpaperOrder";
    private sealed record DraggedWallpaper(WallpaperThumbnailList Owner, string Path);
    private Point _start;
    private string? _dragPath;
    private InsertionMarker? _marker;
    private readonly ObservableCollection<string> _wallpapers = new();
    public static readonly RoutedEvent OrderChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(OrderChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(WallpaperThumbnailList));
    public event RoutedEventHandler OrderChanged
    {
        add => AddHandler(OrderChangedEvent, value);
        remove => RemoveHandler(OrderChangedEvent, value);
    }

    public WallpaperThumbnailList() { AllowDrop = true; ItemsSource = _wallpapers; }

    internal void SetWallpapers(IReadOnlyList<string> paths)
    {
        var keep = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = _wallpapers.Count - 1; index >= 0; index--)
            if (!keep.Contains(_wallpapers[index])) _wallpapers.RemoveAt(index);
        for (var index = 0; index < paths.Count; index++)
        {
            var existing = _wallpapers.IndexOf(paths[index]);
            if (existing < 0) _wallpapers.Insert(index, paths[index]);
            else if (existing != index) _wallpapers.Move(existing, index);
        }
    }

    internal System.Windows.DataObject CreateDragData(string path) =>
        new(DragFormat, new DraggedWallpaper(this, path));

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        _start = e.GetPosition(this);
        _dragPath = (ContainerFromElement(this, e.OriginalSource as DependencyObject) as FrameworkElement)?.DataContext as string;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.LeftButton != MouseButtonState.Pressed) { _dragPath = null; return; }
        if (_dragPath == null || Items.Count < 2) return;
        var delta = e.GetPosition(this) - _start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var path = _dragPath;
        _dragPath = null;
        try { System.Windows.DragDrop.DoDragDrop(this, CreateDragData(path), DragDropEffects.Move); }
        finally { ClearMarker(); }
        e.Handled = true;
    }

    private DraggedWallpaper? ReadDrag(DragEventArgs e) =>
        e.Data.GetDataPresent(DragFormat, false) && e.Data.GetData(DragFormat, false) is DraggedWallpaper drag &&
        ReferenceEquals(drag.Owner, this) && Items.Contains(drag.Path) && (e.AllowedEffects & DragDropEffects.Move) != 0 ? drag : null;

    private (FrameworkElement? Container, int Insertion, bool After) FindTarget(DragEventArgs e)
    {
        var point = e.GetPosition(this);
        // Select the closest thumbnail, including drops in gaps or after the final item.
        FrameworkElement? nearest = null;
        var distance = double.MaxValue;
        var index = 0;
        for (var i = 0; i < Items.Count; i++)
        {
            if (ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container) continue;
            var origin = container.TranslatePoint(new Point(), this);
            var dx = Math.Max(0, Math.Max(origin.X - point.X, point.X - origin.X - container.ActualWidth));
            var dy = Math.Max(0, Math.Max(origin.Y - point.Y, point.Y - origin.Y - container.ActualHeight));
            var candidate = dx * dx + dy * dy;
            if (candidate >= distance) continue;
            nearest = container;
            index = i;
            distance = candidate;
        }
        var after = nearest != null && e.GetPosition(nearest).X >= nearest.ActualWidth / 2;
        return (nearest, index + (after ? 1 : 0), after);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Handled = true;
        e.Effects = ReadDrag(e) == null ? DragDropEffects.None : DragDropEffects.Move;
        ClearMarker();
        if (e.Effects == DragDropEffects.None) return;
        var target = FindTarget(e);
        if (target.Container != null && AdornerLayer.GetAdornerLayer(target.Container) is { } layer)
        {
            _marker = new InsertionMarker(target.Container, target.After, (System.Windows.Media.Brush)FindResource("AccentBrush")) { IsHitTestVisible = false };
            layer.Add(_marker);
        }
        for (DependencyObject? parent = VisualTreeHelper.GetParent(this); parent != null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not ScrollViewer scroll) continue;
            var position = e.GetPosition(scroll);
            if (position.Y < 20) scroll.ScrollToVerticalOffset(scroll.VerticalOffset - 16);
            else if (position.Y > scroll.ActualHeight - 20) scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 16);
            break;
        }
    }

    protected override void OnDragLeave(DragEventArgs e) { base.OnDragLeave(e); ClearMarker(); }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        e.Handled = true;
        ClearMarker();
        e.Effects = DragDropEffects.None;
        if (ReadDrag(e) is not { } drag) return;
        var target = FindTarget(e);
        if (target.Container == null) return;
        var media = Items.Cast<string>().ToList();
        var source = media.IndexOf(drag.Path);
        var destination = target.Insertion - (source < target.Insertion ? 1 : 0);
        e.Effects = DragDropEffects.Move;
        if (source == destination) return;
        _wallpapers.Move(source, destination);
        RaiseEvent(new RoutedEventArgs(OrderChangedEvent));
    }

    private void ClearMarker()
    {
        if (_marker == null) return;
        AdornerLayer.GetAdornerLayer(_marker.AdornedElement)?.Remove(_marker);
        _marker = null;
    }

    private sealed class InsertionMarker(UIElement element, bool after, System.Windows.Media.Brush brush) : Adorner(element)
    {
        protected override void OnRender(DrawingContext context)
        {
            var x = after ? AdornedElement.RenderSize.Width - 4 : 0;
            context.DrawLine(new Pen(brush, 2), new Point(x, 2), new Point(x, AdornedElement.RenderSize.Height - 10));
        }
    }
}
