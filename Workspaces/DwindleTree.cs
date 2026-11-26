using System.Drawing;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Workspaces;

internal enum TileDirection { Left, Right, Up, Down }

/// <summary>
/// The windows of one workspace and where each goes. In the dwindle layout each new window splits the space of the
/// one it opens beside: wide spaces split side by side and tall ones top and bottom, decided again each time the
/// space changes, so splits follow the screen when windows close or a monitor turns. The other layouts go by the
/// order windows opened in and always use all of the space, however many windows there are.
/// </summary>
internal sealed class DwindleTree
{
    private sealed class Node
    {
        public nint Window;
        public Node? Parent, First, Second;
        /// <summary>The user turned this split, so it goes the other way from what its shape suggests.</summary>
        public bool Flipped;
        /// <summary>The direction it was given; windows with a minimum size can force the other one.</summary>
        public bool Placed;
        public Size Minimum;
        public double Ratio = .5;
        public Rectangle Bounds;
        public bool IsLeaf => First == null;
    }

    /// <summary>A tile wider than this, compared to its height, looks stretched, so the grid adds a column first.</summary>
    private const double GridAspect = 1.6;

    private Node? _root;
    private readonly Dictionary<nint, Node> _leaves = new();
    /// <summary>The tiles in order. Swapping trades the windows inside them, so the order follows.</summary>
    private readonly List<Node> _order = new();
    private int _gap;
    private Rectangle _area;
    private double? _mainRatio;
    /// <summary>The share a window keeps when another opens beside it, and the main window's share.</summary>
    public double SplitRatio { get; set; } = .5;
    public TileLayout Mode { get; set; }
    private Func<nint, Size> _minimum = _ => Size.Empty;

    public IReadOnlyCollection<nint> Windows => _leaves.Keys;
    public bool Contains(nint window) => _leaves.ContainsKey(window);

    public void Insert(nint window, nint beside)
    {
        if (_leaves.ContainsKey(window)) return;
        var leaf = new Node { Window = window };
        if (_root == null) _root = leaf;
        else
        {
            var target = _leaves.TryGetValue(beside, out var found) ? found : LastLeaf(_root);
            var split = new Node { Parent = target.Parent, First = target, Second = leaf, Ratio = SplitRatio };
            Replace(target, split);
            target.Parent = split; leaf.Parent = split;
        }
        var after = _leaves.TryGetValue(beside, out var next) ? _order.IndexOf(next) + 1 : _order.Count;
        _order.Insert(after, leaf);
        _leaves[window] = leaf;
    }

    public bool Remove(nint window)
    {
        if (!_leaves.Remove(window, out var leaf)) return false;
        _order.Remove(leaf);
        if (leaf.Parent is not { } parent) { _root = null; return true; }
        var sibling = parent.First == leaf ? parent.Second! : parent.First!;
        Replace(parent, sibling);
        sibling.Parent = parent.Parent;
        return true;
    }

    private void Replace(Node old, Node replacement)
    {
        if (old.Parent is not { } parent) _root = replacement;
        else if (parent.First == old) parent.First = replacement;
        else parent.Second = replacement;
    }

    private static Node LastLeaf(Node node)
    {
        while (!node.IsLeaf) node = node.Second!;
        return node;
    }

    public bool Swap(nint left, nint right)
    {
        if (left == right || !_leaves.TryGetValue(left, out var a) || !_leaves.TryGetValue(right, out var b)) return false;
        (a.Window, b.Window) = (b.Window, a.Window);
        _leaves[left] = b; _leaves[right] = a;
        return true;
    }

    public bool ToggleSplit(nint window)
    {
        if (Mode != TileLayout.Dwindle || !_leaves.TryGetValue(window, out var leaf) || leaf.Parent is not { } parent) return false;
        // Turn it from the way it is now, which minimum sizes may have decided.
        parent.Flipped = !parent.Placed != Wide(parent.Bounds);
        return true;
    }

    private static bool Wide(Rectangle area) => area.Width >= area.Height;

    public IReadOnlyDictionary<nint, Rectangle> Layout(Rectangle area, int gap, Func<nint, Size>? minimum = null)
    {
        _gap = gap;
        _area = area;
        _minimum = minimum ?? (_ => Size.Empty);
        if (_root != null)
        {
            Measure(_root, area);
            if (Mode == TileLayout.Dwindle) Arrange(_root, area);
            else ArrangeInOrder(area);
        }
        return _leaves.ToDictionary(item => item.Key, item => item.Value.Bounds);
    }

    private void Measure(Node node, Rectangle area)
    {
        if (node.IsLeaf) { node.Minimum = _minimum(node.Window); return; }
        // The minimum follows the way the split is likely to go in roughly the space it will get; Arrange turns it if it cannot.
        var across = Wide(area) != node.Flipped;
        var first = (int)Math.Round((across ? area.Width : area.Height) * node.Ratio);
        Measure(node.First!, across ? area with { Width = first } : area with { Height = first });
        Measure(node.Second!, across ? area with { Width = area.Width - first } : area with { Height = area.Height - first });
        node.Minimum = Combine(across, node.First!.Minimum, node.Second!.Minimum);
    }

    private Size Combine(bool sideBySide, Size first, Size second) => sideBySide
        ? new Size(first.Width + _gap + second.Width, Math.Max(first.Height, second.Height))
        : new Size(Math.Max(first.Width, second.Width), first.Height + _gap + second.Height);

    private bool Fits(bool sideBySide, Size first, Size second, Rectangle area)
        => Combine(sideBySide, first, second) is var need && need.Width <= area.Width && need.Height <= area.Height;

    private void Arrange(Node node, Rectangle area)
    {
        node.Bounds = area;
        if (node.IsLeaf) return;
        Size a = node.First!.Minimum, b = node.Second!.Minimum;
        var across = Wide(area) != node.Flipped;
        if (!Fits(across, a, b, area) && Fits(!across, a, b, area)) across = !across;
        node.Placed = across;
        var length = Math.Max(2, (across ? area.Width : area.Height) - _gap);
        var first = Math.Clamp((int)Math.Round(length * node.Ratio), 1, length - 1);
        int least = across ? a.Width : a.Height, rest = across ? b.Width : b.Height;
        if (least + rest <= length) first = Math.Clamp(first, Math.Max(1, least), Math.Min(length - 1, length - rest));
        Arrange(node.First!, across ? area with { Width = first } : area with { Height = first });
        Arrange(node.Second!, across
            ? new Rectangle(area.X + first + _gap, area.Y, length - first, area.Height)
            : new Rectangle(area.X, area.Y + first + _gap, area.Width, length - first));
    }

    /// <summary>How many columns a grid of this many windows gets, so tiles stay close to a comfortable shape.</summary>
    internal static int GridColumns(int count, Rectangle area)
    {
        if (count <= 1 || area.Width <= 0 || area.Height <= 0) return 1;
        var columns = (int)Math.Round(Math.Sqrt(count * area.Width / (area.Height * GridAspect)));
        return Math.Clamp(columns, 1, count);
    }

    private void ArrangeInOrder(Rectangle area)
    {
        var tiles = _order;
        var wide = Wide(area);
        // Columns on a wide screen become rows on a tall one, so the same layout works on a turned monitor.
        Rectangle Strip(Rectangle within, int start, int length, bool across) => across
            ? new Rectangle(within.X + start, within.Y, length, within.Height)
            : new Rectangle(within.X, within.Y + start, within.Width, length);
        int Least(Node node, bool across) => across ? node.Minimum.Width : node.Minimum.Height;
        void Stack(IReadOnlyList<Node> nodes, Rectangle within, bool across)
        {
            var (starts, lengths) = Split(across ? within.Width : within.Height, _gap, nodes.Select(node => Least(node, across)).ToArray());
            for (var i = 0; i < nodes.Count; i++) nodes[i].Bounds = Strip(within, starts[i], lengths[i], across);
        }

        switch (Mode)
        {
            case TileLayout.Columns:
                Stack(tiles, area, wide);
                break;
            case TileLayout.MainAndStack when tiles.Count > 1:
            {
                var ratio = _mainRatio ?? SplitRatio;
                var stack = tiles.Skip(1).ToArray();
                var rest = stack.Max(node => Least(node, wide));
                var (starts, lengths) = Split(wide ? area.Width : area.Height, _gap, new[] { Least(tiles[0], wide), rest }, new[] { ratio, 1 - ratio });
                tiles[0].Bounds = Strip(area, starts[0], lengths[0], wide);
                Stack(stack, Strip(area, starts[1], lengths[1], wide), !wide);
                break;
            }
            case TileLayout.MainAndStack:
                tiles[0].Bounds = area;
                break;
            default:
            {
                // Earlier columns hold fewer windows, so the first ones opened keep the larger tiles.
                var across = wide;
                var columns = GridColumns(tiles.Count, wide ? area : new Rectangle(0, 0, area.Height, area.Width));
                var groups = new List<Node[]>();
                for (int column = 0, taken = 0; column < columns; column++)
                {
                    var size = tiles.Count / columns + (column >= columns - tiles.Count % columns ? 1 : 0);
                    groups.Add(tiles.Skip(taken).Take(size).ToArray());
                    taken += size;
                }
                var (starts, lengths) = Split(across ? area.Width : area.Height, _gap,
                    groups.Select(group => group.Max(node => Least(node, across))).ToArray());
                for (var i = 0; i < groups.Count; i++) Stack(groups[i], Strip(area, starts[i], lengths[i], across), !across);
                break;
            }
        }
    }

    /// <summary>
    /// Shares a length between tiles by weight, giving each at least its minimum when they all fit. Tiles that need
    /// more than their share get their minimum and the rest is shared again among the others.
    /// </summary>
    internal static (int[] Starts, int[] Lengths) Split(int length, int gap, IReadOnlyList<int> minimums, IReadOnlyList<double>? weights = null)
    {
        var count = minimums.Count;
        var available = Math.Max(count, length - gap * (count - 1));
        var weight = Enumerable.Range(0, count).Select(i => weights == null ? 1 : Math.Max(.01, weights[i])).ToArray();
        var sizes = new double[count];
        var fixedAt = new bool[count];
        if (minimums.Sum(value => Math.Max(0, value)) <= available)
            for (var changed = true; changed;)
            {
                changed = false;
                var left = available - Enumerable.Range(0, count).Where(i => fixedAt[i]).Sum(i => sizes[i]);
                var share = Enumerable.Range(0, count).Where(i => !fixedAt[i]).Sum(i => weight[i]);
                for (var i = 0; i < count; i++)
                {
                    if (fixedAt[i]) continue;
                    sizes[i] = left * weight[i] / share;
                    if (sizes[i] < minimums[i]) { sizes[i] = minimums[i]; fixedAt[i] = changed = true; }
                }
            }
        else
        {
            // There is no room for everything: share it out as asked and let the windows overlap.
            var total = weight.Sum();
            for (var i = 0; i < count; i++) sizes[i] = available * weight[i] / total;
        }
        var starts = new int[count];
        var lengths = new int[count];
        double edge = 0;
        for (var i = 0; i < count; i++)
        {
            var start = (int)Math.Round(edge);
            edge += sizes[i];
            var end = i == count - 1 ? available : (int)Math.Round(edge);
            starts[i] = start + gap * i;
            lengths[i] = Math.Max(1, end - start);
        }
        return (starts, lengths);
    }

    /// <summary>Follows edges the user dragged, so neighbors give way.</summary>
    public bool Resize(nint window, Rectangle bounds, int slack = 8)
    {
        if (!_leaves.TryGetValue(window, out var leaf)) return false;
        var old = leaf.Bounds;
        if (Mode == TileLayout.MainAndStack) return ResizeMain(leaf, bounds, slack);
        if (Mode != TileLayout.Dwindle) return false;
        var changed = false;
        if (Math.Abs(bounds.Left - old.Left) > slack) changed |= MoveSplit(leaf, true, true, bounds.Left - _gap);
        if (Math.Abs(bounds.Right - old.Right) > slack) changed |= MoveSplit(leaf, true, false, bounds.Right);
        if (Math.Abs(bounds.Top - old.Top) > slack) changed |= MoveSplit(leaf, false, true, bounds.Top - _gap);
        if (Math.Abs(bounds.Bottom - old.Bottom) > slack) changed |= MoveSplit(leaf, false, false, bounds.Bottom);
        return changed;
    }

    /// <summary>Dragging the edge between the main window and the stack changes the main window's share.</summary>
    private bool ResizeMain(Node leaf, Rectangle bounds, int slack)
    {
        if (_order.Count < 2) return false;
        var wide = Wide(_area);
        var main = _order[0] == leaf;
        var old = leaf.Bounds;
        int edge, was;
        if (wide) { edge = main ? bounds.Right : bounds.Left - _gap; was = main ? old.Right : old.Left - _gap; }
        else { edge = main ? bounds.Bottom : bounds.Top - _gap; was = main ? old.Bottom : old.Top - _gap; }
        if (Math.Abs(edge - was) <= slack) return false;
        var length = (wide ? _area.Width : _area.Height) - _gap;
        if (length <= 0) return false;
        _mainRatio = Math.Clamp((edge - (wide ? _area.X : _area.Y)) / (double)length, .1, .9);
        return true;
    }

    private bool MoveSplit(Node node, bool sideBySide, bool isSecond, int edge)
    {
        for (; node.Parent is { } parent; node = parent)
        {
            if (parent.Placed != sideBySide || parent.Second == node != isSecond) continue;
            var length = (sideBySide ? parent.Bounds.Width : parent.Bounds.Height) - _gap;
            if (length <= 0) return false;
            parent.Ratio = Math.Clamp((edge - (sideBySide ? parent.Bounds.X : parent.Bounds.Y)) / (double)length, .1, .9);
            return true;
        }
        return false;
    }

    public nint At(Point point, nint except = 0)
        => _leaves.FirstOrDefault(item => item.Key != except && item.Value.Bounds.Contains(point)).Key;

    /// <summary>The closest tile on one side, preferring the one most in line.</summary>
    public nint Neighbor(nint window, TileDirection direction)
    {
        if (!_leaves.TryGetValue(window, out var leaf)) return 0;
        var from = leaf.Bounds;
        return _leaves.Where(item => item.Key != window).Select(item => (item.Key, To: item.Value.Bounds))
            .Where(item => direction switch
            {
                TileDirection.Left => item.To.Right <= from.Left, TileDirection.Right => item.To.Left >= from.Right,
                TileDirection.Up => item.To.Bottom <= from.Top, _ => item.To.Top >= from.Bottom
            })
            .OrderBy(item => direction switch
            {
                TileDirection.Left => from.Left - item.To.Right, TileDirection.Right => item.To.Left - from.Right,
                TileDirection.Up => from.Top - item.To.Bottom, _ => item.To.Top - from.Bottom
            })
            .ThenByDescending(item => direction is TileDirection.Left or TileDirection.Right
                ? Math.Min(from.Bottom, item.To.Bottom) - Math.Max(from.Top, item.To.Top)
                : Math.Min(from.Right, item.To.Right) - Math.Max(from.Left, item.To.Left))
            .Select(item => item.Key).FirstOrDefault();
    }
}
