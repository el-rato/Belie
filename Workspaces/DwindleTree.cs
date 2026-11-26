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
        /// <summary>The way its shape suggested when it was last decided: when windows opened or closed, or the screen changed.</summary>
        public bool SideBySide, Oriented;
        /// <summary>The user turned this split, so it goes the other way from what its shape suggests.</summary>
        public bool Flipped;
        /// <summary>The direction it was given; windows with a minimum size can force the other one.</summary>
        public bool Placed;
        public Size Minimum;
        public double Ratio = .5;
        /// <summary>Its share of its column or stack in the layouts other than dwindle, dragged by the user.</summary>
        public double Weight = 1;
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
    private double? _mainRatio;
    private List<Node[]> _groups = new();
    private readonly List<Rectangle> _groupBounds = new();
    private bool _groupsAcross;
    /// <summary>Column sizes dragged in the grid, kept until a window opens or closes.</summary>
    private readonly List<double> _columnWeights = new();
    private int _gridShape;
    /// <summary>The share a window keeps when another opens beside it, and the main window's share.</summary>
    public double SplitRatio { get; set; } = .5;
    public TileLayout Mode { get; set; }
    private TileLayout _arranged;
    /// <summary>Trying every trade gets slow past this many windows.</summary>
    private const int MostToBalance = 10;
    private Func<nint, Size> _minimum = _ => Size.Empty;
    /// <summary>
    /// Splits choose their direction again only when windows open or close or the screen changes. Dragging an edge
    /// must never turn a split, or the tiles would jump away from the pointer.
    /// </summary>
    private bool _reorient = true;
    private Rectangle _lastArea;

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
        parent.Flipped = !parent.Placed != parent.SideBySide;
        return true;
    }

    private static bool Wide(Rectangle area) => area.Width >= area.Height;

    public IReadOnlyDictionary<nint, Rectangle> Layout(Rectangle area, int gap, Func<nint, Size>? minimum = null)
    {
        _gap = gap;
        _minimum = minimum ?? (_ => Size.Empty);
        if (area != _lastArea) { _reorient = true; _lastArea = area; }
        if (Mode != _arranged) { _reorient = true; _arranged = Mode; }
        if (_root != null)
        {
            if (_reorient && _leaves.Count is > 1 and <= MostToBalance && _leaves.Keys.Any(window => !_minimum(window).IsEmpty)) Balance(area);
            Place(area, _reorient);
        }
        return _leaves.ToDictionary(item => item.Key, item => item.Value.Bounds);
    }

    /// <summary>Asks for the splits to be chosen again, as when a window learns it cannot be smaller.</summary>
    public void Rebalance() => _reorient = true;

    private void Place(Rectangle area, bool reorient)
    {
        _reorient = reorient;
        Measure(_root!, area);
        if (Mode != TileLayout.Dwindle) ArrangeInOrder(area);
        else
        {
            Arrange(_root!, area);
            // The first pass guessed each split's space; now each knows the space it really has.
            if (reorient) { Settle(_root!); _reorient = false; Measure(_root!, area); Arrange(_root!, area); }
        }
        _reorient = false;
    }

    private static void Settle(Node node)
    {
        if (node.IsLeaf) return;
        node.SideBySide = Wide(node.Bounds);
        Settle(node.First!); Settle(node.Second!);
    }

    /// <summary>
    /// Apps with a minimum size can leave no room for the others where they happen to be. This trades windows
    /// between tiles while that lets more of them fit, or gives the most squeezed one clearly more room.
    /// </summary>
    private void Balance(Rectangle area)
    {
        Place(area, true);
        var best = Score();
        for (var improved = true; improved;)
        {
            improved = false;
            for (var i = 0; i < _order.Count; i++)
                for (var j = i + 1; j < _order.Count; j++)
                {
                    nint a = _order[i].Window, b = _order[j].Window;
                    Swap(a, b);
                    Place(area, true);
                    var score = Score();
                    if (score.Fit > best.Fit || score.Fit == best.Fit && score.Side >= best.Side * 1.25 + 1) { best = score; improved = true; }
                    else Swap(a, b);
                }
        }
    }

    /// <summary>How many windows get at least their minimum, then the shortest side of any tile.</summary>
    private (int Fit, int Side) Score() => (
        _order.Count(node => node.Bounds.Width >= node.Minimum.Width && node.Bounds.Height >= node.Minimum.Height),
        _order.Min(node => Math.Min(node.Bounds.Width, node.Bounds.Height)));

    private void Measure(Node node, Rectangle area)
    {
        if (node.IsLeaf) { node.Minimum = _minimum(node.Window); return; }
        // The minimum follows the way the split is likely to go in roughly the space it will get; Arrange turns it if it cannot.
        if (_reorient || !node.Oriented) { node.SideBySide = Wide(area); node.Oriented = true; }
        var across = node.SideBySide != node.Flipped;
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
        var across = node.SideBySide != node.Flipped;
        if (!Fits(across, a, b, area) && Fits(!across, a, b, area)) across = !across;
        node.Placed = across;
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

    /// <summary>How many columns a grid of this many windows gets, so tiles stay close to a comfortable shape.</summary>
    internal static int GridColumns(int count, Rectangle area)
    {
        if (count <= 1 || area.Width <= 0 || area.Height <= 0) return 1;
        var columns = (int)Math.Round(Math.Sqrt(count * area.Width / (area.Height * GridAspect)));
        return Math.Clamp(columns, 1, count);
    }

    /// <summary>
    /// Grid, Columns and Main window and a stack are all groups of tiles side by side (or stacked on a tall screen),
    /// each group sharing its space among its own tiles the other way. Every edge between two can be dragged.
    /// </summary>
    private void ArrangeInOrder(Rectangle area)
    {
        var tiles = _order;
        // Columns on a wide screen become rows on a tall one, so the same layout works on a turned monitor.
        var across = Wide(area);
        var groups = new List<Node[]>();
        switch (Mode)
        {
            case TileLayout.Columns:
                groups.AddRange(tiles.Select(node => new[] { node }));
                break;
            case TileLayout.MainAndStack:
                groups.Add(new[] { tiles[0] });
                if (tiles.Count > 1) groups.Add(tiles.Skip(1).ToArray());
                break;
            default:
            {
                // Earlier columns hold fewer windows, so the first ones opened keep the larger tiles.
                var columns = GridColumns(tiles.Count, across ? area : new Rectangle(0, 0, area.Height, area.Width));
                for (int column = 0, taken = 0; column < columns; column++)
                {
                    var size = tiles.Count / columns + (column >= columns - tiles.Count % columns ? 1 : 0);
                    groups.Add(tiles.Skip(taken).Take(size).ToArray());
                    taken += size;
                }
                // Windows change columns when one opens or closes, so sizes dragged for the old shape no longer apply.
                if (_gridShape != tiles.Count)
                {
                    _gridShape = tiles.Count;
                    _columnWeights.Clear();
                    foreach (var node in tiles) node.Weight = 1;
                }
                break;
            }
        }
        _groups = groups; _groupsAcross = across; _groupBounds.Clear();
        var (starts, lengths) = Split(Length(area, across), _gap, groups.Select(group => group.Max(node => Least(node, across))).ToArray(),
            Enumerable.Range(0, groups.Count).Select(GroupWeight).ToArray());
        for (var i = 0; i < groups.Count; i++)
        {
            var strip = Strip(area, starts[i], lengths[i], across);
            _groupBounds.Add(strip);
            var (at, sizes) = Split(Length(strip, !across), _gap, groups[i].Select(node => Least(node, !across)).ToArray(),
                groups[i].Select(node => node.Weight).ToArray());
            for (var j = 0; j < groups[i].Length; j++) groups[i][j].Bounds = Strip(strip, at[j], sizes[j], !across);
        }
    }

    private static int Length(Rectangle area, bool across) => across ? area.Width : area.Height;
    private static int Start(Rectangle area, bool across) => across ? area.X : area.Y;
    private static int End(Rectangle area, bool across) => across ? area.Right : area.Bottom;
    private static int Least(Node node, bool across) => across ? node.Minimum.Width : node.Minimum.Height;
    private static Rectangle Strip(Rectangle within, int start, int length, bool across) => across
        ? new Rectangle(within.X + start, within.Y, length, within.Height)
        : new Rectangle(within.X, within.Y + start, within.Width, length);

    private double GroupWeight(int group) => Mode switch
    {
        TileLayout.Columns => _groups.Count > group ? _groups[group][0].Weight : 1,
        TileLayout.MainAndStack => group == 0 ? _mainRatio ?? SplitRatio : 1 - (_mainRatio ?? SplitRatio),
        _ => group < _columnWeights.Count ? _columnWeights[group] : 1
    };

    /// <summary>Gives the first of two neighboring groups this share of the space the two have together.</summary>
    private void ShareGroups(int first, double share)
    {
        if (Mode == TileLayout.MainAndStack) { _mainRatio = share; return; }
        var total = GroupWeight(first) + GroupWeight(first + 1);
        if (Mode == TileLayout.Columns) { _groups[first][0].Weight = total * share; _groups[first + 1][0].Weight = total * (1 - share); return; }
        while (_columnWeights.Count < _groups.Count) _columnWeights.Add(1);
        _columnWeights[first] = total * share; _columnWeights[first + 1] = total * (1 - share);
    }

    /// <summary>Moves the edge between two neighbors to where the user dragged it. False when it did not move.</summary>
    private bool MoveEdge(Rectangle first, Rectangle second, int edge, bool across, Action<double> share)
    {
        var total = End(second, across) - Start(first, across) - _gap;
        if (total <= 0 || edge == End(first, across)) return false;
        share(Math.Clamp((edge - Start(first, across)) / (double)total, .05, .95));
        return true;
    }

    private bool ResizeInOrder(Node leaf, Rectangle bounds, int slack)
    {
        var group = _groups.FindIndex(nodes => nodes.Contains(leaf));
        if (group < 0) return false;
        var old = leaf.Bounds;
        var changed = false;
        // Edges between groups.
        var across = _groupsAcross;
        if (Math.Abs(Start(bounds, across) - Start(old, across)) > slack && group > 0)
            changed |= MoveEdge(_groupBounds[group - 1], _groupBounds[group], Start(bounds, across) - _gap, across, share => ShareGroups(group - 1, share));
        if (Math.Abs(End(bounds, across) - End(old, across)) > slack && group < _groups.Count - 1)
            changed |= MoveEdge(_groupBounds[group], _groupBounds[group + 1], End(bounds, across), across, share => ShareGroups(group, share));
        // Edges between tiles in the same group.
        var nodes = _groups[group];
        var index = Array.IndexOf(nodes, leaf);
        void Share(Node first, Node second, double share)
        {
            var total = first.Weight + second.Weight;
            first.Weight = total * share; second.Weight = total * (1 - share);
        }
        if (Math.Abs(Start(bounds, !across) - Start(old, !across)) > slack && index > 0)
            changed |= MoveEdge(nodes[index - 1].Bounds, old, Start(bounds, !across) - _gap, !across, share => Share(nodes[index - 1], leaf, share));
        if (Math.Abs(End(bounds, !across) - End(old, !across)) > slack && index < nodes.Length - 1)
            changed |= MoveEdge(old, nodes[index + 1].Bounds, End(bounds, !across), !across, share => Share(leaf, nodes[index + 1], share));
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
        if (Mode != TileLayout.Dwindle) return ResizeInOrder(leaf, bounds, slack);
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
