using System.Drawing;
using System.Runtime.InteropServices;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Workspaces;

/// <param name="Frame">The visible edges, without Windows' invisible resize borders.</param>
/// <param name="FixedSize">It has no edges to resize it by. Belie sizes it anyway when <see cref="TilingEngine.TileFixedSize"/> is on.</param>
internal readonly record struct TileWindow(bool CanTile, bool Minimized, bool Maximized, Rectangle Frame, bool Floats = false, bool FixedSize = false);
internal readonly record struct TileMove(nint Window, string Monitor, bool Hidden, bool IsNew, bool IsCorrection, Rectangle From, Rectangle To);

internal interface ITileSystem
{
    TileWindow Describe(nint window);
    Rectangle WorkArea(string monitor);
    /// <summary>The smallest the window says it can be, or empty when it does not say.</summary>
    Size MinimumSize(nint window);
    /// <summary>False when Windows does not allow Belie to move the window.</summary>
    bool Place(nint window, Rectangle frame);
    void Restore(nint window);
    void Maximize(nint window);
}

/// <summary>Arranges each workspace's windows so they fill the screen without overlapping.</summary>
internal sealed class TilingEngine
{
    private sealed record Original(Rectangle Frame, bool Maximized);

    private readonly ITileSystem _system;
    private readonly Dictionary<(string Monitor, int Index), DwindleTree> _trees = new();
    private readonly Dictionary<nint, Rectangle> _placed = new();
    private readonly Dictionary<nint, long> _placedAt = new();
    private readonly Dictionary<nint, int> _corrections = new();
    private readonly Dictionary<nint, (Size Size, long Asked, long Wait)> _minimums = new();
    /// <summary>Sizes a window would not grow past, learned by watching it; 0 is no limit. It is centered in a larger tile.</summary>
    private readonly Dictionary<nint, Size> _maximums = new();
    private readonly HashSet<nint> _restore = new();
    private readonly Func<long> _now;
    private readonly Dictionary<(string Monitor, int Index), nint> _lastFocused = new();
    private readonly Dictionary<nint, Original> _originals = new();
    private readonly HashSet<nint> _floating = new();
    private readonly HashSet<nint> _refused = new();

    public int Gap { get; set; } = 6;
    public int Margin { get; set; } = 6;
    /// <summary>A window that is alone on its workspace fills it, without gaps.</summary>
    public bool SmartGaps { get; set; }
    public double SplitRatio { get; set; } = .5;
    public TileLayout Layout { get; set; }
    /// <summary>Windows that cannot be resized by their edges are tiled too.</summary>
    public bool TileFixedSize { get; set; } = true;

    public TilingEngine(ITileSystem system, Func<long>? now = null) { _system = system; _now = now ?? (() => Environment.TickCount64); }

    private Size Minimum(nint window)
    {
        // An app that is still starting may not answer, so a missing answer is asked for again. Most that give none
        // have none, and asking a busy app means waiting for it, so the questions become rarer.
        var asked = _minimums.TryGetValue(window, out var known);
        if (asked && (!known.Size.IsEmpty || _now() - known.Asked < known.Wait)) return known.Size;
        var size = _system.MinimumSize(window);
        _minimums[window] = (size, _now(), asked ? Math.Min(120_000, known.Wait * 3) : 2000);
        return size;
    }

    public bool IsTiled(nint window) => TreeOf(window) != null;
    private DwindleTree? TreeOf(nint window) => _trees.Values.FirstOrDefault(tree => tree.Contains(window));

    private Rectangle Area(string monitor, bool alone = false)
    {
        var area = _system.WorkArea(monitor);
        if (!alone || !SmartGaps) area.Inflate(-Margin, -Margin);
        return area;
    }

    /// <summary>Matches the layout to the windows that exist now and returns the moves it calls for.</summary>
    public IReadOnlyList<TileMove> Update(IReadOnlyList<WorkspaceWindow> windows, nint focused)
    {
        var known = windows.ToDictionary(window => window.Handle);
        foreach (var tree in _trees.Values)
            foreach (var gone in tree.Windows.Where(window => !known.ContainsKey(window)).ToArray()) tree.Remove(gone);
        foreach (var set in new[] { _floating, _refused }) set.RemoveWhere(window => !known.ContainsKey(window));
        foreach (var gone in _originals.Keys.Where(window => !known.ContainsKey(window)).ToArray()) { _originals.Remove(gone); Forget(gone); _minimums.Remove(gone); _maximums.Remove(gone); _restore.Remove(gone); }
        // A window that just opened already has focus, so it splits the tile that had it before.
        foreach (var (key, tree) in _trees) if (tree.Contains(focused)) _lastFocused[key] = focused;

        var added = new HashSet<nint>();
        var maximized = new HashSet<nint>();
        foreach (var window in windows)
        {
            var key = (window.Monitor, window.Index);
            var current = TreeOf(window.Handle);
            var state = _system.Describe(window.Handle);
            var belongs = !state.Minimized && !state.Floats && (TileFixedSize || !state.FixedSize) && !_floating.Contains(window.Handle) && !_refused.Contains(window.Handle)
                && (current != null || state.CanTile && !window.Hidden);
            if (current != null && (!belongs || current != _trees.GetValueOrDefault(key))) { current.Remove(window.Handle); Forget(window.Handle); current = null; }
            if (!belongs) continue;
            if (current == null)
            {
                if (!_trees.TryGetValue(key, out var tree)) _trees[key] = tree = new DwindleTree();
                tree.SplitRatio = SplitRatio; tree.Mode = Layout;
                if (_originals.TryAdd(window.Handle, new Original(state.Frame, state.Maximized)))
                {
                    added.Add(window.Handle);
                    if (state.Maximized) _restore.Add(window.Handle);
                }
                else if (state.Maximized) maximized.Add(window.Handle);
                tree.Insert(window.Handle, _lastFocused.GetValueOrDefault(key));
            }
            // A window maximized later stays that way, and keeps its place for when it is restored.
            else if (state.Maximized && !_restore.Contains(window.Handle)) maximized.Add(window.Handle);
        }
        foreach (var key in _trees.Where(item => item.Value.Windows.Count == 0).Select(item => item.Key).ToArray()) _trees.Remove(key);

        var moves = new List<TileMove>();
        var learned = false;
        foreach (var (key, tree) in _trees)
            foreach (var (window, target) in Targets(key.Monitor, tree))
            {
                if (maximized.Contains(window)) { _placed.Remove(window); continue; }
                var frame = _system.Describe(window).Frame;
                var settled = _placed.TryGetValue(window, out var last) && last == target;
                if (!settled) _corrections.Remove(window);
                else
                {
                    // Borders are only known once a window is restored, and some apps resize themselves after opening.
                    // Others have a minimum size, so this gives up instead of arguing with them.
                    if (known[window].Hidden || Near(frame, target)) continue;
                    // Windows are moved in the background, so a busy app may simply not have got there yet.
                    if (_now() - _placedAt.GetValueOrDefault(window) < 250) continue;
                    // It went where it was told but is larger: that is as small as it gets. Smaller: as large as it gets.
                    var least = Minimum(window);
                    var tries = _corrections.GetValueOrDefault(window);
                    if (Math.Abs(frame.X - target.X) <= 2 && Math.Abs(frame.Y - target.Y) <= 2)
                    {
                        bool wider = frame.Width > Math.Max(target.Width, least.Width) + 2, taller = frame.Height > Math.Max(target.Height, least.Height) + 2;
                        // A window still catching up after a drag also looks too small, so it is asked once more first.
                        bool narrower = frame.Width < target.Width - 2, shorter = frame.Height < target.Height - 2, refused = (narrower || shorter) && tries >= 1;
                        if (wider || taller)
                            _minimums[window] = (new Size(wider ? frame.Width : least.Width, taller ? frame.Height : least.Height), _now(), 2000);
                        if (refused)
                        {
                            var most = _maximums.GetValueOrDefault(window);
                            _maximums[window] = new Size(narrower ? frame.Width : most.Width, shorter ? frame.Height : most.Height);
                        }
                        if (wider || taller || refused) { learned = true; continue; }
                    }
                    if (tries >= 3) continue;
                    _corrections[window] = tries + 1;
                }
                moves.Add(new TileMove(window, key.Monitor, known[window].Hidden, added.Contains(window), settled, frame, target));
            }
        return learned ? Update(windows, focused) : moves;
    }

    private IEnumerable<(nint Window, Rectangle Target)> Targets(string monitor, DwindleTree tree)
    {
        var area = Area(monitor, tree.Windows.Count == 1);
        tree.Mode = Layout; tree.SplitRatio = SplitRatio;
        foreach (var (window, bounds) in tree.Layout(area, Gap, Minimum)) yield return (window, Fit(bounds, area, Minimum(window), _maximums.GetValueOrDefault(window)));
    }

    /// <summary>
    /// Where a window goes in its tile. One that will not shrink to the tile overlaps its neighbors rather than leave the
    /// screen; one that will not grow to fill it is centered in it.
    /// </summary>
    internal static Rectangle Fit(Rectangle tile, Rectangle area, Size least, Size most)
    {
        static int Length(int tile, int least, int most, int room)
        {
            var length = Math.Max(tile, Math.Min(least, room));
            return most > 0 && most < length ? Math.Max(most, Math.Min(least, room)) : length;
        }
        var width = Length(tile.Width, least.Width, most.Width, area.Width);
        var height = Length(tile.Height, least.Height, most.Height, area.Height);
        int x = tile.X + Math.Max(0, tile.Width - width) / 2, y = tile.Y + Math.Max(0, tile.Height - height) / 2;
        return new Rectangle(Math.Max(area.X, Math.Min(x, area.Right - width)), Math.Max(area.Y, Math.Min(y, area.Bottom - height)), width, height);
    }

    private void Forget(nint window) { _placed.Remove(window); _placedAt.Remove(window); _corrections.Remove(window); }

    private bool Place(nint window, Rectangle target)
    {
        if (!_system.Place(window, target)) return false;
        _placed[window] = target; _placedAt[window] = _now();
        return true;
    }

    private static bool Near(Rectangle left, Rectangle right) => Math.Abs(left.Left - right.Left) <= 2 && Math.Abs(left.Top - right.Top) <= 2
        && Math.Abs(left.Right - right.Right) <= 2 && Math.Abs(left.Bottom - right.Bottom) <= 2;

    public void Apply(IReadOnlyList<TileMove> moves)
    {
        foreach (var move in moves)
        {
            if (TreeOf(move.Window) is not { } tree) continue;
            if (_restore.Remove(move.Window)) _system.Restore(move.Window);
            if (Place(move.Window, move.To)) continue;
            // Apps running as administrator cannot be moved, so the others should not leave room for them.
            Logger.Warn("Windows would not let a window be tiled; it will float.");
            tree.Remove(move.Window); _refused.Add(move.Window);
        }
    }

    /// <summary>The user is dragging a window's edge; its neighbors follow.</summary>
    public void UserResizing(nint window)
    {
        var (key, tree) = _trees.FirstOrDefault(item => item.Value.Contains(window));
        if (tree == null || !_placed.TryGetValue(window, out var placed)) return;
        var frame = _system.Describe(window).Frame;
        if (Math.Abs(frame.Width - placed.Width) <= 8 && Math.Abs(frame.Height - placed.Height) <= 8) return; // Moving, not resizing.
        if (!tree.Resize(window, frame, 2)) return;
        foreach (var (other, target) in Targets(key.Monitor, tree))
        {
            if (other == window || _placed.TryGetValue(other, out var last) && (last == target
                // Let a slow app finish one size before asking for the next.
                || !Near(_system.Describe(other).Frame, last) && _now() - _placedAt.GetValueOrDefault(other) < 150)) continue;
            Place(other, target);
        }
    }

    /// <summary>The user finished dragging or resizing a window.</summary>
    public void UserMoved(nint window, Point cursor)
    {
        if (TreeOf(window) is not { } tree || !_placed.Remove(window, out var placed)) return;
        var frame = _system.Describe(window).Frame;
        if (Math.Abs(frame.Width - placed.Width) > 8 || Math.Abs(frame.Height - placed.Height) > 8) { _maximums.Remove(window); tree.Resize(window, frame); }
        else if (tree.At(cursor, window) is var other && other != 0) tree.Swap(window, other);
    }

    public nint Neighbor(nint window, TileDirection direction) => TreeOf(window)?.Neighbor(window, direction) ?? 0;
    public bool Swap(nint window, TileDirection direction) => TreeOf(window) is { } tree
        && tree.Neighbor(window, direction) is var other && other != 0 && tree.Swap(window, other);
    public bool ToggleSplit(nint window) => TreeOf(window)?.ToggleSplit(window) == true;

    public void ToggleFloating(nint window)
    {
        if (_floating.Remove(window)) return;
        if (TreeOf(window) is not { } tree) return;
        _floating.Add(window); tree.Remove(window); Forget(window);
        // Return it to the size it had before tiling, or a floating window would look the same as a tile.
        if (_originals.TryGetValue(window, out var original) && !original.Maximized) _system.Place(window, original.Frame);
    }

    /// <summary>Puts windows back where they were before they were tiled.</summary>
    public void Release()
    {
        foreach (var (window, original) in _originals)
        {
            if (_refused.Contains(window) || TreeOf(window) == null) continue;
            _system.Place(window, original.Frame);
            if (original.Maximized) _system.Maximize(window);
        }
        _trees.Clear(); _placed.Clear(); _placedAt.Clear(); _corrections.Clear(); _minimums.Clear(); _maximums.Clear(); _restore.Clear(); _lastFocused.Clear(); _originals.Clear(); _floating.Clear(); _refused.Clear();
    }
}

internal sealed class NativeTileSystem : ITileSystem
{
    private const int WsThickFrame = 0x40000, WsMaximizeBox = 0x10000, WsCaption = 0xC00000, WsExTopmost = 0x8, WsMaximize = 0x1000000;
    private const uint SwpAsync = 0x4000;
    private readonly Func<IReadOnlyCollection<string>> _floatingApps;

    /// <param name="floatingApps">Process names whose windows are left alone.</param>
    public NativeTileSystem(Func<IReadOnlyCollection<string>>? floatingApps = null) => _floatingApps = floatingApps ?? (() => Array.Empty<string>());

    private readonly Dictionary<nint, (int Left, int Top, int Right, int Bottom)> _borders = new();
    private readonly Dictionary<(uint Process, string Class), (int Left, int Top, int Right, int Bottom)> _kindBorders = new();

    private static (uint, string) Kind(nint window)
    {
        NativeMethods.GetWindowThreadProcessId(window, out var process);
        return (process, NativeMethods.ClassName(window));
    }

    public TileWindow Describe(nint window)
    {
        var style = NativeMethods.GetWindowLong(window, NativeMethods.GWL_STYLE);
        var extended = NativeMethods.GetWindowLong(window, NativeMethods.GWL_EXSTYLE);
        NativeMethods.GetWindowRect(window, out var rect);
        var frame = Frame(window, rect);
        var info = new NativeMethods.MONITORINFOEX { Size = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(), Device = "" };
        var fullscreen = NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(window, NativeMethods.MONITOR_DEFAULTTONEAREST), ref info)
            && rect.Left <= info.Monitor.Left && rect.Top <= info.Monitor.Top && rect.Right >= info.Monitor.Right && rect.Bottom >= info.Monitor.Bottom;
        var maximized = (style & WsMaximize) != 0;
        // Dialogs and panels belong to another window, often a hidden one, and float unless they have their own taskbar button.
        var standsAlone = NativeMethods.GetWindow(window, NativeMethods.GW_OWNER) == IntPtr.Zero || (extended & NativeMethods.WS_EX_APPWINDOW) != 0;
        // A fullscreen window is left alone just like one the user maximized. Picture-in-picture players and call
        // windows keep their own size and place.
        var (tiles, fixedSize) = Shape(style);
        return new TileWindow(standsAlone && tiles && (extended & WsExTopmost) == 0 && (maximized || !fullscreen),
            NativeMethods.IsIconic(window), maximized || fullscreen, frame, Floats(window) || Popouts.Is(window), fixedSize);
    }

    /// <summary>
    /// Whether a window's style makes it an app window to tile, and whether it has no way to be resized. Apps that
    /// draw their own frame, such as Firefox, drop the resizable-edge and title-bar styles but keep a maximize
    /// button, which says they are meant to change size. Without any of these, it is a splash screen or a panel.
    /// </summary>
    internal static (bool Tiles, bool FixedSize) Shape(int style)
    {
        var resizable = (style & (WsThickFrame | WsMaximizeBox)) != 0;
        return (resizable || (style & WsCaption) == WsCaption, !resizable);
    }

    private bool Floats(nint window)
    {
        var apps = _floatingApps();
        if (apps.Count == 0) return false;
        NativeMethods.GetWindowThreadProcessId(window, out var process);
        return process != 0 && apps.Contains(NativeMethods.ProcessInfo(process).Name, StringComparer.OrdinalIgnoreCase);
    }

    private Rectangle Frame(nint window, NativeMethods.RECT rect)
    {
        var visible = NativeMethods.IsWindowVisible(window) && !NativeMethods.IsIconic(window);
        if (visible && (NativeMethods.GetWindowLong(window, NativeMethods.GWL_STYLE) & WsMaximize) == 0
            && NativeMethods.DwmGetWindowAttribute(window, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out NativeMethods.RECT bounds, Marshal.SizeOf<NativeMethods.RECT>()) == 0)
        {
            var border = (bounds.Left - rect.Left, bounds.Top - rect.Top, rect.Right - bounds.Right, rect.Bottom - bounds.Bottom);
            // Anything larger is a window caught halfway through moving, not a border.
            if (new[] { border.Item1, border.Item2, border.Item3, border.Item4 }.All(side => side is >= 0 and <= 32))
            {
                _borders[window] = border;
                // Menus and tooltips can share a main window's class but have no border.
                if (border.Item1 > 0) _kindBorders[Kind(window)] = border;
            }
        }
        var (left, top, right, bottom) = _borders.GetValueOrDefault(window);
        return Rectangle.FromLTRB(rect.Left + left, rect.Top + top, rect.Right - right, rect.Bottom - bottom);
    }

    public Rectangle WorkArea(string monitor)
        => System.Windows.Forms.Screen.AllScreens.FirstOrDefault(screen => screen.DeviceName == monitor)?.DeviceName is { } name ? Work(name) : Rectangle.Empty;

    // Screen caches working areas, and docking the bar changes them.
    private static Rectangle Work(string device)
    {
        var result = Rectangle.Empty;
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new NativeMethods.MONITORINFOEX { Size = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(), Device = "" };
            if (!NativeMethods.GetMonitorInfo(monitor, ref info) || info.Device != device) return true;
            result = Rectangle.FromLTRB(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
            return false;
        }, IntPtr.Zero);
        return result;
    }

    public bool Place(nint window, Rectangle frame)
    {
        if (frame.Width < 1 || frame.Height < 1) return true;
        var (left, top, right, bottom) = Border(window);
        return NativeMethods.SetWindowPos(window, IntPtr.Zero, frame.X - left, frame.Y - top, frame.Width + left + right, frame.Height + top + bottom,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | SwpAsync) || !NativeMethods.IsWindow(window);
    }

    /// <summary>A maximized window hides its border, so this goes by the app's other windows, then by what Windows usually draws.</summary>
    private (int Left, int Top, int Right, int Bottom) Border(nint window)
    {
        if (_borders.TryGetValue(window, out var border) || _kindBorders.TryGetValue(Kind(window), out border)) return border;
        var side = (int)(7 * MonitorDpi(window) / 96);
        return (side, 0, side, side);
    }

    private static uint MonitorDpi(nint window)
        => NativeMethods.GetDpiForMonitor(NativeMethods.MonitorFromWindow(window, NativeMethods.MONITOR_DEFAULTTONEAREST), 0, out var dpi, out _) == 0 ? dpi : 96;

    public Size MinimumSize(nint window)
    {
        var info = new NativeMethods.MINMAXINFO();
        const uint getMinMaxInfo = 0x24, abortIfHung = 2, patience = 50;
        if (NativeMethods.SendMessageTimeout(window, getMinMaxInfo, 0, ref info, abortIfHung, patience, out _) == 0) return Size.Empty;
        // Apps that ignore the display's scale answer in their own smaller units.
        var dpi = NativeMethods.GetDpiForWindow(window);
        var scale = dpi == 0 ? 1 : Math.Max(1, MonitorDpi(window) / (double)dpi);
        var (left, top, right, bottom) = Border(window);
        int Visible(int size, int border) => size <= 0 ? 0 : Math.Max(0, (int)Math.Ceiling(size * scale) - border);
        return new Size(Visible(info.MinTrackWidth, left + right), Visible(info.MinTrackHeight, top + bottom));
    }

    public void Restore(nint window) => NativeMethods.ShowWindowAsync(window, NativeMethods.SW_SHOWNOACTIVATE);
    public void Maximize(nint window) => NativeMethods.ShowWindowAsync(window, NativeMethods.SW_MAXIMIZE);
}
