using System.Drawing;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Engine;

/// <summary>
/// Says when each square cell of the screen changes to the new wallpaper, as a fraction of the transition.
/// Pure arithmetic, so the same seed always gives the same transition.
/// </summary>
internal sealed class TransitionField
{
    /// <summary>The outline is drawn first; cells start changing after this.</summary>
    public const float OutlineEnd = .4f;

    public int Columns { get; }
    public int Rows { get; }
    public float[] Arrival { get; }
    /// <summary>Cell indexes, earliest first.</summary>
    public int[] Order { get; }
    /// <summary>The subject's outline in cells; empty for colonies.</summary>
    public IReadOnlyList<PointF[]> Lines { get; }

    private TransitionField(int columns, int rows, float[] arrival, IReadOnlyList<PointF[]>? lines = null)
    {
        Columns = columns; Rows = rows; Arrival = arrival; Lines = lines ?? Array.Empty<PointF[]>();
        Order = Enumerable.Range(0, arrival.Length).ToArray();
        Array.Sort((float[])arrival.Clone(), Order);
    }

    /// <summary>Patches that land in random places and eat through the old wallpaper.</summary>
    public static TransitionField Colonies(int columns, int rows, int seed)
    {
        var random = new Random(seed);
        var noise = new Noise(random.Next());
        var reach = (float)Math.Sqrt(columns * (double)columns + rows * (double)rows);
        var colonies = Enumerable.Range(0, 7 + random.Next(5)).Select(index => (
            X: (float)random.NextDouble() * columns, Y: (float)random.NextDouble() * rows,
            Birth: index == 0 ? 0 : (float)random.NextDouble() * .16f * reach, Speed: .75f + (float)random.NextDouble() * .5f)).ToArray();
        var times = new float[columns * rows];
        Parallel.For(0, rows, TransitionPainter.Share, y =>
        {
            for (var x = 0; x < columns; x++)
            {
                var first = float.MaxValue;
                foreach (var colony in colonies)
                    first = Math.Min(first, colony.Birth + MathF.Sqrt((x - colony.X) * (x - colony.X) + (y - colony.Y) * (y - colony.Y)) / colony.Speed);
                times[y * columns + x] = first + Rough(noise, x, y, reach);
            }
        });
        Spread(times, 0, 1, Enumerable.Range(0, times.Length).ToArray());
        return new TransitionField(columns, rows, times);
    }

    /// <summary>A circle that opens from a point, or closes on one.</summary>
    public static TransitionField Circle(int columns, int rows, float x, float y, bool closing)
        => From(columns, rows, (column, row) =>
        {
            var distance = MathF.Sqrt((column - x * columns) * (column - x * columns) + (row - y * rows) * (row - y * rows));
            return closing ? -distance : distance;
        });

    /// <summary>A front that crosses the screen. Waves ripple along it.</summary>
    public static TransitionField Wipe(int columns, int rows, double degrees, bool wave)
    {
        float across = (float)Math.Cos(degrees * Math.PI / 180), down = (float)Math.Sin(degrees * Math.PI / 180);
        var reach = Math.Max(columns, rows);
        return From(columns, rows, (x, y) => x * across + y * down + (wave ? MathF.Sin((y * across - x * down) * MathF.Tau * 4 / reach) * reach * .05f : 0));
    }

    /// <summary>Strips that all open at once, across the given direction.</summary>
    public static TransitionField Blinds(int columns, int rows, double degrees)
    {
        float across = (float)Math.Cos(degrees * Math.PI / 180), down = (float)Math.Sin(degrees * Math.PI / 180);
        var width = Math.Max(4f, Math.Max(columns, rows) / 14f);
        return From(columns, rows, (x, y) => { var at = (x * across + y * down) / width; return at - MathF.Floor(at); });
    }

    /// <summary>Arms that sweep around the middle.</summary>
    public static TransitionField Spiral(int columns, int rows, float turn)
    {
        var reach = MathF.Sqrt(columns * columns + rows * rows) / 2;
        return From(columns, rows, (x, y) =>
        {
            float dx = x - columns / 2f, dy = y - rows / 2f, distance = MathF.Sqrt(dx * dx + dy * dy) / reach;
            var angle = (MathF.Atan2(dy, dx) / MathF.Tau + turn + 2 + distance * .8f) % 1f;
            return angle + distance * .5f;
        });
    }

    /// <summary>Cells in no particular order.</summary>
    public static TransitionField Dissolve(int columns, int rows, int seed)
    {
        var random = new Random(seed);
        var times = new float[columns * rows];
        for (var i = 0; i < times.Length; i++) times[i] = (float)random.NextDouble();
        return new TransitionField(columns, rows, times);
    }

    private static TransitionField From(int columns, int rows, Func<int, int, float> when)
    {
        var times = new float[columns * rows];
        Parallel.For(0, rows, TransitionPainter.Share, y => { for (var x = 0; x < columns; x++) times[y * columns + x] = when(x, y); });
        Spread(times, 0, 1, Enumerable.Range(0, times.Length).ToArray());
        return new TransitionField(columns, rows, times);
    }

    private static readonly WallpaperTransitionStyle[] Choices =
    {
        WallpaperTransitionStyle.Colonies, WallpaperTransitionStyle.Grow, WallpaperTransitionStyle.Shrink, WallpaperTransitionStyle.Wipe, WallpaperTransitionStyle.Wave,
        WallpaperTransitionStyle.Blinds, WallpaperTransitionStyle.Spiral, WallpaperTransitionStyle.Dissolve, WallpaperTransitionStyle.Fade
    };

    /// <summary>The style to play, which differs from the chosen one only when that is random.</summary>
    public static WallpaperTransitionStyle Resolve(WallpaperTransitionStyle style, Random random)
        => style == WallpaperTransitionStyle.Random ? Choices[random.Next(Choices.Length)] : style;

    /// <summary>Null for a fade, which has no cells.</summary>
    public static TransitionField? For(TransitionOptions options, int columns, int rows, Func<bool[]?> subject, Random random) => options.Style switch
    {
        WallpaperTransitionStyle.Fade => null,
        WallpaperTransitionStyle.Grow or WallpaperTransitionStyle.Shrink => Circle(columns, rows, .2f + .6f * (float)random.NextDouble(), .2f + .6f * (float)random.NextDouble(), options.Style == WallpaperTransitionStyle.Shrink),
        WallpaperTransitionStyle.Wipe or WallpaperTransitionStyle.Wave => Wipe(columns, rows, options.Angle, options.Style == WallpaperTransitionStyle.Wave),
        WallpaperTransitionStyle.Blinds => Blinds(columns, rows, options.Angle),
        WallpaperTransitionStyle.Spiral => Spiral(columns, rows, (float)random.NextDouble()),
        WallpaperTransitionStyle.Dissolve => Dissolve(columns, rows, random.Next()),
        WallpaperTransitionStyle.Automatic when subject() is { } mask => Outline(columns, rows, mask, random.Next()),
        _ => Colonies(columns, rows, random.Next())
    };

    /// <summary>Draws the subject's outline, fills it, then spreads out from it.</summary>
    public static TransitionField Outline(int columns, int rows, bool[] mask, int seed)
    {
        var noise = new Noise(seed);
        var reach = (float)Math.Sqrt(columns * (double)columns + rows * (double)rows);
        var inside = Distance(columns, rows, mask, from: false);
        var outside = Distance(columns, rows, mask, from: true);
        var times = new float[columns * rows];
        Parallel.For(0, rows, TransitionPainter.Share, y =>
        {
            for (var x = 0; x < columns; x++)
            {
                var index = y * columns + x;
                times[index] = mask[index] ? inside[index] + Rough(noise, x, y, reach) * .5f : outside[index] + Rough(noise, x, y, reach);
            }
        });
        Spread(times, OutlineEnd, .62f, Enumerable.Range(0, times.Length).Where(index => mask[index]).ToArray());
        Spread(times, .55f, 1, Enumerable.Range(0, times.Length).Where(index => !mask[index]).ToArray());
        return new TransitionField(columns, rows, times, Trace(columns, rows, mask));
    }

    /// <summary>Ragged edges: large lobes, smaller ones, then single cells.</summary>
    private static float Rough(Noise noise, int x, int y, float reach)
        => ((noise.At(x / 46f, y / 46f) - .5f) * .2f + (noise.At(x / 15f + 70, y / 15f) - .5f) * .075f + (noise.At(x / 4f, y / 4f + 90) - .5f) * .02f) * reach;

    /// <summary>Fits times between two moments. Part rank, so the last corner does not take forever to fill.</summary>
    private static void Spread(float[] times, float start, float end, int[] cells)
    {
        if (cells.Length == 0) return;
        Array.Sort(cells.Select(cell => times[cell]).ToArray(), cells);
        float least = times[cells[0]], range = Math.Max(1e-6f, times[cells[^1]] - least);
        for (var rank = 0; rank < cells.Length; rank++)
        {
            var value = .6f * (times[cells[rank]] - least) / range + .4f * rank / Math.Max(1, cells.Length - 1);
            times[cells[rank]] = start + (end - start) * value;
        }
    }

    /// <summary>Approximate distance to the nearest cell that is (or is not) part of the mask.</summary>
    private static float[] Distance(int columns, int rows, bool[] mask, bool from)
    {
        var result = new float[mask.Length];
        for (var i = 0; i < mask.Length; i++) result[i] = mask[i] == from ? 0 : 1e6f;
        void Look(int x, int y, int dx, int dy, float step)
        {
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= columns || ny >= rows) return;
            result[y * columns + x] = Math.Min(result[y * columns + x], result[ny * columns + nx] + step);
        }
        for (var y = 0; y < rows; y++)
            for (var x = 0; x < columns; x++) { Look(x, y, -1, 0, 1); Look(x, y, 0, -1, 1); Look(x, y, -1, -1, 1.414f); Look(x, y, 1, -1, 1.414f); }
        for (var y = rows - 1; y >= 0; y--)
            for (var x = columns - 1; x >= 0; x--) { Look(x, y, 1, 0, 1); Look(x, y, 0, 1, 1); Look(x, y, 1, 1, 1.414f); Look(x, y, -1, 1, 1.414f); }
        return result;
    }

    /// <summary>Follows the edges between mask and background. Edges along the screen's border are not drawn.</summary>
    private static List<PointF[]> Trace(int columns, int rows, bool[] mask)
    {
        bool In(int x, int y) => x >= 0 && y >= 0 && x < columns && y < rows && mask[y * columns + x];
        var next = new Dictionary<Point, List<Point>>();
        var ends = new HashSet<Point>();
        void Edge(Point start, Point end)
        {
            if (!next.TryGetValue(start, out var list)) next[start] = list = new List<Point>(1);
            list.Add(end); ends.Add(end);
        }
        for (var y = 0; y < rows; y++)
            for (var x = 0; x < columns; x++)
            {
                if (!In(x, y)) continue;
                // Directed, so each loop keeps the mask on one side.
                if (y > 0 && !In(x, y - 1)) Edge(new Point(x, y), new Point(x + 1, y));
                if (x < columns - 1 && !In(x + 1, y)) Edge(new Point(x + 1, y), new Point(x + 1, y + 1));
                if (y < rows - 1 && !In(x, y + 1)) Edge(new Point(x + 1, y + 1), new Point(x, y + 1));
                if (x > 0 && !In(x - 1, y)) Edge(new Point(x, y + 1), new Point(x, y));
            }
        var lines = new List<PointF[]>();
        // Open lines have to start at their own end; closed ones can start anywhere.
        foreach (var start in next.Keys.OrderBy(point => ends.Contains(point)).ToArray())
            while (next.TryGetValue(start, out var first) && first.Count > 0)
            {
                var chain = new List<Point> { start };
                var at = start;
                while (next.TryGetValue(at, out var options) && options.Count > 0)
                {
                    at = options[^1]; options.RemoveAt(options.Count - 1);
                    chain.Add(at);
                }
                if (chain.Count >= 30) lines.Add(Smooth(chain));
            }
        return lines;
    }

    private static PointF[] Smooth(List<Point> chain)
    {
        const int around = 7;
        var closed = chain[0] == chain[^1];
        var result = new PointF[chain.Count];
        for (var i = 0; i < chain.Count; i++)
        {
            float x = 0, y = 0; var count = 0;
            for (var offset = -around; offset <= around; offset++)
            {
                var at = i + offset;
                if (closed) at = ((at % (chain.Count - 1)) + chain.Count - 1) % (chain.Count - 1);
                else if (at < 0 || at >= chain.Count) continue;
                x += chain[at].X; y += chain[at].Y; count++;
            }
            result[i] = new PointF(x / count, y / count);
        }
        return result;
    }

    private sealed class Noise
    {
        private readonly int _seed;
        public Noise(int seed) => _seed = seed;

        private float Corner(int x, int y)
        {
            unchecked
            {
                var hash = (uint)(x * 374761393 + y * 668265263 + _seed * 69069);
                hash = (hash ^ (hash >> 13)) * 1274126177;
                return ((hash ^ (hash >> 16)) & 0xFFFF) / 65535f;
            }
        }

        public float At(float x, float y)
        {
            int left = (int)MathF.Floor(x), top = (int)MathF.Floor(y);
            float fx = x - left, fy = y - top;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            var upper = Corner(left, top) + (Corner(left + 1, top) - Corner(left, top)) * fx;
            var lower = Corner(left, top + 1) + (Corner(left + 1, top + 1) - Corner(left, top + 1)) * fx;
            return upper + (lower - upper) * fy;
        }
    }
}

/// <summary>
/// Finds a picture's subject when it clearly stands out from a plain background. It prefers giving up:
/// a wrong outline looks worse than none.
/// </summary>
internal static class SubjectFinder
{
    /// <param name="pixels">Packed 0xRRGGBB, row by row.</param>
    public static bool[]? Find(int[] pixels, int width, int height)
    {
        if (width < 32 || height < 18 || pixels.Length != width * height) return null;
        var colors = pixels.Select(pixel => new[] { (pixel >> 16 & 255) / 255f, (pixel >> 8 & 255) / 255f, (pixel & 255) / 255f }).ToArray();
        // People and objects usually touch the bottom, so it says nothing about the background.
        var edge = Enumerable.Range(0, width * height)
            .Where(index => index / width < height / 14 || index % width < width / 20 || index % width >= width - width / 20)
            .Where(index => index / width < height * 4 / 5).ToArray();
        var background = Cluster(edge.Select(index => colors[index]).ToArray(), 3);
        var distance = Blur(colors.Select(color => background.Min(other => Difference(color, other))).ToArray(), width, height);

        var edgeDistance = edge.Average(index => distance[index]);
        var (threshold, separation) = Threshold(distance);
        if (separation < .6f || edgeDistance > .03f) return null;
        // On a plain background even a dark or faint subject can be told apart.
        var plain = edgeDistance < .006f;
        if (plain) threshold = Math.Min(threshold, Math.Max(.014f, edgeDistance * 6));
        else if (threshold < .09f) return null;
        var mask = distance.Select(value => value > threshold).ToArray();
        mask = Grow(Grow(mask, width, height, true), width, height, false);
        mask = Largest(mask, width, height);
        FillHoles(mask, width, height);

        var area = mask.Count(inside => inside) / (float)mask.Length;
        if (area < (plain ? .025f : .05f) || area > .55f) return null;
        // Something reaching the top and sides is scenery, not a subject.
        if (edge.Count(index => mask[index]) > edge.Length * .1f) return null;
        var boundary = 0;
        for (var y = 1; y < height - 1; y++)
            for (var x = 1; x < width - 1; x++)
                if (mask[y * width + x] && (!mask[y * width + x - 1] || !mask[y * width + x + 1] || !mask[(y - 1) * width + x] || !mask[(y + 1) * width + x])) boundary++;
        return boundary > 11 * Math.Sqrt(area * mask.Length) ? null : mask; // Too ragged to be one thing.
    }

    /// <summary>
    /// The subject from a saved cut-out, laid over the screen as the picture is.
    /// <paramref name="place"/> is where the whole picture lands, as fractions of the screen.
    /// </summary>
    public static bool[]? FromCutout(byte[] map, RectangleF place, int width, int height)
    {
        if (map.Length != SubjectModel.MapSize * SubjectModel.MapSize || place.Width <= 0 || place.Height <= 0) return null;
        const int side = SubjectModel.MapSize;
        var mask = new bool[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                float u = ((x + .5f) / width - place.X) / place.Width, v = ((y + .5f) / height - place.Y) / place.Height;
                if (u is < 0 or >= 1 || v is < 0 or >= 1) continue;
                mask[y * width + x] = map[(int)(v * side) * side + (int)(u * side)] >= 128;
            }
        mask = Grow(Grow(mask, width, height, false), width, height, true); // Drops specks and threads.
        mask = Largest(mask, width, height);
        FillHoles(mask, width, height);
        var area = mask.Count(inside => inside) / (float)mask.Length;
        // The model calls a whole poster "foreground" now and then, or finds only a crumb.
        if (area < .02f || area > .6f) return null;
        // A subject may run off one side, as a car in the corner does; something spread along two is scenery.
        double Share(Func<int, int, bool> band)
        {
            int inside = 0, all = 0;
            for (var y = 0; y < height * 4 / 5; y++)
                for (var x = 0; x < width; x++)
                    if (band(x, y)) { all++; if (mask[y * width + x]) inside++; }
            return all == 0 ? 0 : inside / (double)all;
        }
        var sides = new[]
        {
            Share((x, y) => y < height / 14), Share((x, y) => x < width / 20), Share((x, y) => x >= width - width / 20)
        }.Count(share => share > .4);
        return sides >= 2 ? null : mask;
    }

    public static bool[] Scale(bool[] mask, int width, int height, int columns, int rows)
    {
        var result = new bool[columns * rows];
        float At(int x, int y) => mask[Math.Clamp(y, 0, height - 1) * width + Math.Clamp(x, 0, width - 1)] ? 1 : 0;
        for (var y = 0; y < rows; y++)
            for (var x = 0; x < columns; x++)
            {
                float sx = (x + .5f) * width / columns - .5f, sy = (y + .5f) * height / rows - .5f;
                int left = (int)MathF.Floor(sx), top = (int)MathF.Floor(sy);
                float fx = sx - left, fy = sy - top;
                var value = (At(left, top) * (1 - fx) + At(left + 1, top) * fx) * (1 - fy) + (At(left, top + 1) * (1 - fx) + At(left + 1, top + 1) * fx) * fy;
                result[y * columns + x] = value >= .5f;
            }
        return result;
    }

    private static float Difference(float[] a, float[] b)
    {
        float red = a[0] - b[0], green = a[1] - b[1], blue = a[2] - b[2];
        return MathF.Sqrt(.3f * red * red + .5f * green * green + .2f * blue * blue);
    }

    private static List<float[]> Cluster(float[][] samples, int count)
    {
        // Starts from colors far apart, so the result does not depend on chance.
        var centers = new List<float[]> { samples[samples.Length / 2] };
        while (centers.Count < count) centers.Add(samples.MaxBy(sample => centers.Min(center => Difference(sample, center)))!);
        var sizes = new int[count];
        for (var round = 0; round < 6; round++)
        {
            var sums = Enumerable.Range(0, count).Select(_ => new float[3]).ToArray();
            Array.Clear(sizes);
            foreach (var sample in samples)
            {
                var nearest = 0;
                for (var i = 1; i < count; i++) if (Difference(sample, centers[i]) < Difference(sample, centers[nearest])) nearest = i;
                sizes[nearest]++;
                for (var channel = 0; channel < 3; channel++) sums[nearest][channel] += sample[channel];
            }
            for (var i = 0; i < count; i++) if (sizes[i] > 0) centers[i] = sums[i].Select(sum => sum / sizes[i]).ToArray();
        }
        return centers.Where((_, i) => sizes[i] >= samples.Length * .08).ToList() is { Count: > 0 } common ? common : centers;
    }

    private static float[] Blur(float[] values, int width, int height)
    {
        var result = new float[values.Length];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                float sum = 0; var count = 0;
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                        if (x + dx >= 0 && x + dx < width && y + dy >= 0 && y + dy < height) { sum += values[(y + dy) * width + x + dx]; count++; }
                result[y * width + x] = sum / count;
            }
        return result;
    }

    /// <summary>The split between near and far values, and how cleanly it separates them (0 to 1).</summary>
    private static (float Threshold, float Separation) Threshold(float[] values)
    {
        const int bins = 64;
        var histogram = new int[bins];
        var top = Math.Max(1e-6f, values.Max());
        foreach (var value in values) histogram[Math.Min(bins - 1, (int)(value / top * bins))]++;
        double total = values.Length, mean = 0, variance = 0;
        for (var i = 0; i < bins; i++) mean += i * histogram[i] / total;
        for (var i = 0; i < bins; i++) variance += (i - mean) * (i - mean) * histogram[i] / total;
        double weight = 0, sum = 0, best = 0; var at = 0;
        for (var i = 0; i < bins; i++)
        {
            weight += histogram[i] / total; sum += i * histogram[i] / total;
            if (weight <= 0 || weight >= 1) continue;
            var between = Math.Pow(mean * weight - sum, 2) / (weight * (1 - weight));
            if (between > best) { best = between; at = i; }
        }
        return ((at + 1) * top / bins, variance <= 0 ? 0 : (float)(best / variance));
    }

    private static bool[] Grow(bool[] mask, int width, int height, bool grow)
    {
        var result = (bool[])mask.Clone();
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (mask[y * width + x] == grow) continue;
                if (x > 0 && mask[y * width + x - 1] == grow || x < width - 1 && mask[y * width + x + 1] == grow
                    || y > 0 && mask[(y - 1) * width + x] == grow || y < height - 1 && mask[(y + 1) * width + x] == grow) result[y * width + x] = grow;
            }
        return result;
    }

    private static List<List<int>> Areas(bool[] mask, int width, int height, bool value)
    {
        var seen = new bool[mask.Length];
        var areas = new List<List<int>>();
        for (var start = 0; start < mask.Length; start++)
        {
            if (seen[start] || mask[start] != value) continue;
            var area = new List<int> { start };
            seen[start] = true;
            for (var i = 0; i < area.Count; i++)
            {
                int x = area[i] % width, y = area[i] / width;
                foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                {
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    var next = ny * width + nx;
                    if (seen[next] || mask[next] != value) continue;
                    seen[next] = true; area.Add(next);
                }
            }
            areas.Add(area);
        }
        return areas;
    }

    private static bool[] Largest(bool[] mask, int width, int height)
    {
        var areas = Areas(mask, width, height, true);
        var result = new bool[mask.Length];
        if (areas.Count == 0) return result;
        var largest = areas.Max(area => area.Count);
        foreach (var index in areas.Where(area => area.Count >= largest * .2).SelectMany(area => area)) result[index] = true;
        return result;
    }

    private static void FillHoles(bool[] mask, int width, int height)
    {
        foreach (var hole in Areas(mask, width, height, false))
            if (!hole.Any(index => index % width == 0 || index % width == width - 1 || index < width || index >= mask.Length - width))
                foreach (var index in hole) mask[index] = true;
    }
}
