using System.Drawing;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Workspaces;

internal enum TileDirection { Left, Right, Up, Down }

/// <summary>
/// The windows of one workspace and where each goes, the way Hyprland tiles. In dwindle each new window splits the
/// tile of the one it opens beside, side by side when that tile is wide and top and bottom when it is tall. In master
/// the first window takes one side and the rest share the other.
/// </summary>
internal sealed class DwindleTree
{
    private sealed class Node
    {
        public nint Window;
        public Node? Parent, First, Second;
        /// <summary>The way its shape said to split, decided when windows open or close or the screen changes.</summary>
        public bool SideBySide = true;
        /// <summary>The user turned this split.</summary>
        public bool Flipped;
        /// <summary>The way it was split last time, which minimum sizes can turn.</summary>
        public bool Across = true;
        public double Ratio = .5;
        /// <summary>Its share of the stack in master, dragged by the user.</summary>
        public double Weight = 1;
        public Rectangle Bounds;
        public bool IsLeaf => First == null;
    }

    private Node? _root;
    private readonly Dictionary<nint, Node> _leaves = new();
    /// <summary>The tiles in order. Swapping trades the windows inside them, so the order follows.</summary>
    private readonly List<Node> _order = new();
    private int _gap;
    private double? _mainRatio;
    private Rectangle _mainBounds, _stackBounds;
    private bool _across;
    /// <summary>The share a window keeps when another opens beside it, and the main window's share.</summary>
    public double SplitRatio { get; set; } = .5;
    public TileLayout Mode { get; set; }
    private Func<nint, Size> _minimum = _ => Size.Empty;
    /// <summary>
    /// Splits choose their direction again only when windows open or close or the screen changes. Dragging an edge
    /// must never turn a split, or the tiles would jump away from the pointer.
    /// </summary>
    private bool _reorient = true;
    private Rectangle _lastArea;
    private TileLayout _arranged;

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
            // Like Hyprland, the split goes the way the tile it splits is shaped.
            var wide = target.Bounds.IsEmpty || Wide(target.Bounds);
            var split = new Node { Parent = target.Parent, First = target, Second = leaf, Ratio = SplitRatio, SideBySide = wide, Across = wide };
            Replace(target, split);
            target.Parent = split; leaf.Parent = split;
        }
        _reorient = true;
        var after = _leaves.TryGetValue(beside, out var next) ? _order.IndexOf(next) + 1 : _order.Count;
        _order.Insert(after, leaf);
        _leaves[window] = leaf;
    }

    public bool Remove(nint window)
    {
        if (!_leaves.Remove(window, out var leaf)) return false;
        _order.Remove(leaf);
        _reorient = true;
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
        parent.Flipped = !parent.Across != parent.SideBySide;
        return true;
    }

    private static bool Wide(Rectangle area) => area.Width >= area.Height;

    public IReadOnlyDictionary<nint, Rectangle> Layout(Rectangle area, int gap, Func<nint, Size>? minimum = null)
    {
        _gap = gap;
        _minimum = minimum ?? (_ => Size.Empty);
        if (area != _lastArea || Mode != _arranged) { _reorient = true; _lastArea = area; _arranged = Mode; }
        if (_root != null)
        {
            if (Mode == TileLayout.MainAndStack) ArrangeMaster(area);
            else
            {
                Arrange(_root, area);
                // Splits that just turned change what their tiles need, so the sizes are worked out once more.
                if (_reorient) { _reorient = false; Arrange(_root, area); }
            }
        }
        _reorient = false;
        return _leaves.ToDictionary(item => item.Key, item => item.Value.Bounds);
    }

    /// <summary>Asks for the splits to be chosen again, as when a window turns out to have a minimum size.</summary>
    public void Rebalance() => _reorient = true;

    /// <summary>
    /// The least room a tile and everything in it needs, given roughly the space it will get: split the way each
    /// split went last time, or the way its share of that space is shaped when splits are being chosen again.
    /// </summary>
    private Size Need(Node node, Rectangle area)
    {
        if (node.IsLeaf) return _minimum(node.Window);
        var across = _reorient ? Wide(area) != node.Flipped : node.Across;
        var first = (int)Math.Round((across ? area.Width : area.Height) * node.Ratio);
        return Combine(across, Need(node.First!, across ? area with { Width = first } : area with { Height = first }),
            Need(node.Second!, across ? area with { Width = area.Width - first } : area with { Height = area.Height - first }));
    }

    private Size Combine(bool across, Size first, Size second) => across
        ? new Size(first.Width + _gap + second.Width, Math.Max(first.Height, second.Height))
        : new Size(Math.Max(first.Width, second.Width), first.Height + _gap + second.Height);

    private bool Fits(bool across, Size first, Size second, Rectangle area)
        => Combine(across, first, second) is var need && need.Width <= area.Width && need.Height <= area.Height;

    private void Arrange(Node node, Rectangle area)
    {
        node.Bounds = area;
        if (node.IsLeaf) return;
        if (_reorient) node.SideBySide = Wide(area);
        var across = node.SideBySide != node.Flipped;
        var guess = (int)Math.Round((across ? area.Width : area.Height) * node.Ratio);
        Size a = Need(node.First!, across ? area with { Width = guess } : area with { Height = guess }),
            b = Need(node.Second!, across ? area with { Width = area.Width - guess } : area with { Height = area.Height - guess });
        // A window that cannot be that narrow (or short) gets the split turned the other way, if that fits.
        if (!Fits(across, a, b, area) && Fits(!across, a, b, area)) across = !across;
        node.Across = across;
        var length = Math.Max(2, (across ? area.Width : area.Height) - _gap);
        var first = Math.Clamp((int)Math.Round(length * node.Ratio), 1, length - 1);
        int least = across ? a.Width : a.Height, rest = across ? b.Width : b.Height;
        // A window can need the whole length, which leaves no room to clamp into.
        int low = Math.Max(1, least), high = Math.Min(length - 1, length - rest);
        if (least + rest <= length && low <= high) first = Math.Clamp(first, low, high);
        Arrange(node.First!, across ? area with { Width = first } : area with { Height = first });
        Arrange(node.Second!, across
            ? new Rectangle(area.X + first + _gap, area.Y, length - first, area.Height)
            : new Rectangle(area.X, area.Y + first + _gap, area.Width, length - first));
    }

    /// <summary>The first window on one side and the rest stacked on the other, turned on a tall screen.</summary>
    private void ArrangeMaster(Rectangle area)
    {
        _across = Wide(area);
        var main = _order[0];
        var stack = _order.Skip(1).ToArray();
        if (stack.Length == 0) { main.Bounds = _mainBounds = area; _stackBounds = Rectangle.Empty; return; }
        var ratio = _mainRatio ?? SplitRatio;
        var (starts, lengths) = Split(Length(area, _across), _gap,
            new[] { Least(_minimum(main.Window), _across), stack.Max(node => Least(_minimum(node.Window), _across)) }, new[] { ratio, 1 - ratio });
        main.Bounds = _mainBounds = Strip(area, starts[0], lengths[0], _across);
        _stackBounds = Strip(area, starts[1], lengths[1], _across);
        var (at, sizes) = Split(Length(_stackBounds, !_across), _gap, stack.Select(node => Least(_minimum(node.Window), !_across)).ToArray(),
            stack.Select(node => node.Weight).ToArray());
        for (var i = 0; i < stack.Length; i++) stack[i].Bounds = Strip(_stackBounds, at[i], sizes[i], !_across);
    }

    private static int Length(Rectangle area, bool across) => across ? area.Width : area.Height;
    private static int Start(Rectangle area, bool across) => across ? area.X : area.Y;
    private static int End(Rectangle area, bool across) => across ? area.Right : area.Bottom;
    private static int Least(Size minimum, bool across) => across ? minimum.Width : minimum.Height;
    private static Rectangle Strip(Rectangle within, int start, int length, bool across) => across
        ? new Rectangle(within.X + start, within.Y, length, within.Height)
        : new Rectangle(within.X, within.Y + start, within.Width, length);

    /// <summary>Moves the edge between two neighbors to where the user dragged it. False when it did not move.</summary>
    private bool MoveEdge(Rectangle first, Rectangle second, int edge, bool across, Action<double> share)
    {
        var total = End(second, across) - Start(first, across) - _gap;
        if (total <= 0 || edge == End(first, across)) return false;
        share(Math.Clamp((edge - Start(first, across)) / (double)total, .05, .95));
        return true;
    }

    private bool ResizeMaster(Node leaf, Rectangle bounds, int slack)
    {
        if (_stackBounds.IsEmpty) return false;
        var old = leaf.Bounds;
        var changed = false;
        var isMain = leaf == _order[0];
        // The edge between the main window and the stack.
        var moved = isMain ? End(bounds, _across) - End(old, _across) : Start(bounds, _across) - Start(old, _across);
        if (Math.Abs(moved) > slack)
            changed |= MoveEdge(_mainBounds, _stackBounds, isMain ? End(bounds, _across) : Start(bounds, _across) - _gap, _across, share => _mainRatio = share);
        if (isMain) return changed;
        // Edges between windows in the stack.
        var stack = _order.Skip(1).ToArray();
        var index = Array.IndexOf(stack, leaf);
        void Share(Node first, Node second, double share)
        {
            var total = first.Weight + second.Weight;
            first.Weight = total * share; second.Weight = total * (1 - share);
        }
        if (Math.Abs(Start(bounds, !_across) - Start(old, !_across)) > slack && index > 0)
            changed |= MoveEdge(stack[index - 1].Bounds, old, Start(bounds, !_across) - _gap, !_across, share => Share(stack[index - 1], leaf, share));
        if (Math.Abs(End(bounds, !_across) - End(old, !_across)) > slack && index < stack.Length - 1)
            changed |= MoveEdge(old, stack[index + 1].Bounds, End(bounds, !_across), !_across, share => Share(leaf, stack[index + 1], share));
        return changed;
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
        if (Mode != TileLayout.Dwindle) return ResizeMaster(leaf, bounds, slack);
        var changed = false;
        if (Math.Abs(bounds.Left - old.Left) > slack) changed |= MoveSplit(leaf, true, true, bounds.Left - _gap);
        if (Math.Abs(bounds.Right - old.Right) > slack) changed |= MoveSplit(leaf, true, false, bounds.Right);
        if (Math.Abs(bounds.Top - old.Top) > slack) changed |= MoveSplit(leaf, false, true, bounds.Top - _gap);
        if (Math.Abs(bounds.Bottom - old.Bottom) > slack) changed |= MoveSplit(leaf, false, false, bounds.Bottom);
        return changed;
    }

    private bool MoveSplit(Node node, bool sideBySide, bool isSecond, int edge)
    {
        for (; node.Parent is { } parent; node = parent)
        {
            if (parent.Across != sideBySide || parent.Second == node != isSecond) continue;
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
