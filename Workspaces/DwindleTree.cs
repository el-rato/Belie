using System.Drawing;

namespace WallpaperProfiles.Workspaces;

internal enum TileDirection { Left, Right, Up, Down }

/// <summary>
/// A tiling layout where each new window splits the space of the one it opens beside. Wide spaces split side by
/// side and tall ones top and bottom; a split then keeps its direction until it is toggled.
/// </summary>
internal sealed class DwindleTree
{
    private sealed class Node
    {
        public nint Window;
        public Node? Parent, First, Second;
        /// <summary>The wanted direction. Windows with a minimum size can force the other one; see <see cref="Placed"/>.</summary>
        public bool SideBySide;
        public bool Placed;
        public Size Minimum;
        public double Ratio = .5;
        public Rectangle Bounds;
        public bool IsLeaf => First == null;
    }

    private Node? _root;
    private readonly Dictionary<nint, Node> _leaves = new();
    private int _gap;
    /// <summary>The share a window keeps when another opens beside it.</summary>
    public double SplitRatio { get; set; } = .5;
    private Func<nint, Size> _minimum = _ => Size.Empty;

    public IReadOnlyCollection<nint> Windows => _leaves.Keys;
    public bool Contains(nint window) => _leaves.ContainsKey(window);

    public void Insert(nint window, nint beside, Rectangle area, int gap, Func<nint, Size>? minimum = null)
    {
        if (_leaves.ContainsKey(window)) return;
        var leaf = new Node { Window = window };
        if (_root == null) _root = leaf;
        else
        {
            Layout(area, gap, minimum);
            var target = _leaves.TryGetValue(beside, out var found) ? found : LastLeaf(_root);
            var wide = target.Bounds.Width >= target.Bounds.Height;
            Size first = _minimum(target.Window), second = _minimum(window);
            // Split the other way rather than squeezing every other window to make room.
            if (!Fits(wide, first, second, target.Bounds) && Fits(!wide, first, second, target.Bounds)) wide = !wide;
            var split = new Node { Parent = target.Parent, SideBySide = wide, First = target, Second = leaf, Ratio = SplitRatio };
            Replace(target, split);
            target.Parent = split; leaf.Parent = split;
        }
        _leaves[window] = leaf;
    }

    public bool Remove(nint window)
    {
        if (!_leaves.Remove(window, out var leaf)) return false;
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
        if (!_leaves.TryGetValue(window, out var leaf) || leaf.Parent == null) return false;
        leaf.Parent.SideBySide = !leaf.Parent.Placed;
        return true;
    }

    public IReadOnlyDictionary<nint, Rectangle> Layout(Rectangle area, int gap, Func<nint, Size>? minimum = null)
    {
        _gap = gap;
        _minimum = minimum ?? (_ => Size.Empty);
        if (_root != null) { Measure(_root); Arrange(_root, area); }
        return _leaves.ToDictionary(item => item.Key, item => item.Value.Bounds);
    }

    private void Measure(Node node)
    {
        if (node.IsLeaf) { node.Minimum = _minimum(node.Window); return; }
        Measure(node.First!); Measure(node.Second!);
        node.Minimum = Combine(node.SideBySide, node.First!.Minimum, node.Second!.Minimum);
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
        var across = node.SideBySide;
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

    /// <summary>Follows edges the user dragged, so neighbors give way.</summary>
    public bool Resize(nint window, Rectangle bounds, int slack = 8)
    {
        if (!_leaves.TryGetValue(window, out var leaf)) return false;
        var old = leaf.Bounds;
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
