using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Engine;

/// <summary>Paints a transition into a picture in memory, over whatever that picture already shows.</summary>
internal sealed unsafe class TransitionPainter : IDisposable
{
    /// <summary>Leaves most of the processor to the apps on screen.</summary>
    internal static readonly ParallelOptions Share = new() { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 4) };

    private readonly int _width, _height;
    private readonly int* _pixels;
    private readonly Bitmap _shown;
    private Bitmap? _picture;
    private BitmapData? _next;
    private int[]? _before;
    private TransitionField? _field;
    private Color _glow, _line;
    private int _cell = 1, _lit, _done;
    private float _burn, _dim, _dimmed;
    private readonly List<int> _drawn = new();

    /// <param name="pixels">Rows of 32-bit pixels, without gaps.</param>
    public TransitionPainter(IntPtr pixels, int width, int height)
    {
        _pixels = (int*)pixels; _width = width; _height = height;
        _shown = new Bitmap(width, height, width * 4, PixelFormat.Format32bppRgb, pixels);
    }

    /// <param name="field">Null fades the whole picture.</param>
    public void Begin(Bitmap next, TransitionField? field, Color glow, Color line, TransitionOptions options)
    {
        Detach();
        (_picture, _field, _glow, _line, _cell) = (next, field, glow, line, options.Cell);
        _burn = options.Glow ? (float)options.GlowLength : 0;
        _next = next.LockBits(new Rectangle(0, 0, _width, _height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        _lit = _done = 0;
        // Only an outline has a moment in which the old picture is all there is.
        _dim = field?.Lines.Count > 0 ? (float)options.Dim : 0;
        _dimmed = 0;
        _drawn.Clear();
        if (field != null) _drawn.AddRange(Enumerable.Repeat(0, field.Lines.Count));
        if (field == null || _dim > 0)
        {
            _before = new int[_width * _height];
            System.Runtime.InteropServices.Marshal.Copy((IntPtr)_pixels, _before, 0, _before.Length);
        }
    }

    public void Detach()
    {
        if (_next != null) _picture?.UnlockBits(_next);
        _next = null; _picture = null; _field = null; _before = null;
    }

    /// <returns>The part that changed.</returns>
    public Rectangle Draw(float progress)
    {
        if (_next == null) return Rectangle.Empty;
        if (_field == null) return Fade(progress);
        var dirty = new Dirty();
        if (_dimmed < _dim) Dim(progress, ref dirty);
        if (_field.Lines.Count > 0) DrawLines(progress, ref dirty);
        // The end is brought forward so that the last cells have finished glowing in time.
        var time = progress * (1 + _burn);
        while (_lit < _field.Order.Length && _field.Arrival[_field.Order[_lit]] <= time)
        {
            if (_burn > 0) Fill(_field.Order[_lit], glow: true, ref dirty);
            _lit++;
        }
        while (_done < _lit && (progress >= 1 || _field.Arrival[_field.Order[_done]] + _burn <= time)) Fill(_field.Order[_done++], glow: false, ref dirty);
        return dirty.IsEmpty ? Rectangle.Empty : Rectangle.Intersect(dirty.Bounds, new Rectangle(0, 0, _width, _height));
    }

    private Rectangle Fade(float progress)
    {
        // Slow at both ends.
        var amount = (uint)Math.Round(256 * progress * progress * (3 - 2 * progress));
        var rest = 256 - amount;
        var source = (byte*)_next!.Scan0;
        var stride = _next.Stride;
        var before = _before!;
        Parallel.For(0, _height, Share, y =>
        {
            var to = (uint*)(source + (long)y * stride);
            var target = (uint*)_pixels + (long)y * _width;
            fixed (int* first = &before[y * _width])
            {
                var from = (uint*)first;
                for (var x = 0; x < _width; x++)
                    target[x] = ((from[x] & 0xFF00FF) * rest + (to[x] & 0xFF00FF) * amount) >> 8 & 0xFF00FF
                        | ((from[x] & 0xFF00) * rest + (to[x] & 0xFF00) * amount) >> 8 & 0xFF00;
            }
        });
        return new Rectangle(0, 0, _width, _height);
    }

    /// <summary>Darkens the old picture. It is done before the first cell changes, so it only ever touches that.</summary>
    private void Dim(float progress, ref Dirty dirty)
    {
        var time = Math.Min(1, progress / (TransitionField.OutlineEnd * .6f));
        _dimmed = time >= 1 ? _dim : _dim * time * time * (3 - 2 * time);
        var rest = (uint)Math.Round(256 * (1 - _dimmed));
        var before = _before!;
        Parallel.For(0, _height, Share, y =>
        {
            var target = (uint*)_pixels + (long)y * _width;
            fixed (int* first = &before[y * _width])
            {
                var from = (uint*)first;
                for (var x = 0; x < _width; x++) target[x] = (from[x] & 0xFF00FF) * rest >> 8 & 0xFF00FF | (from[x] & 0xFF00) * rest >> 8 & 0xFF00;
            }
        });
        for (var i = 0; i < _drawn.Count; i++) _drawn[i] = 0; // The lines went with it.
        dirty.Add(0, 0, _width, _height);
        if (_dimmed >= _dim) _before = null;
    }

    private void DrawLines(float progress, ref Dirty dirty)
    {
        var drawing = Math.Min(1, progress / TransitionField.OutlineEnd);
        drawing = 1 - (1 - drawing) * (1 - drawing); // Quick at first, then careful.
        using var graphics = Graphics.FromImage(_shown);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(_line, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        for (var i = 0; i < _drawn.Count; i++)
        {
            var line = _field!.Lines[i];
            var until = (int)Math.Round(drawing * (line.Length - 1));
            if (until <= _drawn[i]) continue;
            var points = line[_drawn[i]..(until + 1)].Select(point => new PointF(point.X * _cell, point.Y * _cell)).ToArray();
            graphics.DrawLines(pen, points);
            foreach (var point in points) dirty.Add((int)point.X - 3, (int)point.Y - 3, 7, 7);
            _drawn[i] = until;
        }
        graphics.Flush(FlushIntention.Sync);
    }

    private void Fill(int cell, bool glow, ref Dirty dirty)
    {
        int left = cell % _field!.Columns * _cell, top = cell / _field.Columns * _cell;
        int right = Math.Min(left + _cell, _width), bottom = Math.Min(top + _cell, _height);
        for (var y = top; y < bottom; y++)
        {
            var source = (int*)((byte*)_next!.Scan0 + (long)y * _next.Stride);
            var target = _pixels + (long)y * _width;
            for (var x = left; x < right; x++)
            {
                var pixel = source[x];
                target[x] = !glow ? pixel : ((pixel >> 16 & 255) * 2 + _glow.R * 3) / 5 << 16 | ((pixel >> 8 & 255) * 2 + _glow.G * 3) / 5 << 8 | ((pixel & 255) * 2 + _glow.B * 3) / 5;
            }
        }
        dirty.Add(left, top, _cell, _cell);
    }

    public void Dispose() { Detach(); _shown.Dispose(); }

    private struct Dirty
    {
        private int _left, _top, _right, _bottom;
        public readonly bool IsEmpty => _right <= _left;
        public readonly Rectangle Bounds => Rectangle.FromLTRB(_left, _top, _right, _bottom);
        public void Add(int x, int y, int width, int height)
        {
            if (IsEmpty) { (_left, _top, _right, _bottom) = (x, y, x + width, y + height); return; }
            _left = Math.Min(_left, x); _top = Math.Min(_top, y); _right = Math.Max(_right, x + width); _bottom = Math.Max(_bottom, y + height);
        }
    }
}
