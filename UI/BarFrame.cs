using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using Point = System.Windows.Point;

namespace WallpaperProfiles.UI;

/// <summary>A border that can also be a notch: joined to the screen edge, with corners that curve out to meet it.</summary>
internal sealed class BarFrame : Border
{
    private bool _notch, _atBottom;

    public void Shape(bool notch, bool atBottom)
    {
        if (_notch == notch && _atBottom == atBottom) return;
        (_notch, _atBottom) = (notch, atBottom);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        if (!_notch) { base.OnRender(context); return; }
        double width = RenderSize.Width, height = RenderSize.Height;
        if (width < 1 || height < 1) return;
        var round = Math.Min(CornerRadius.BottomLeft > 0 ? CornerRadius.BottomLeft : CornerRadius.TopLeft, Math.Min(width / 2, height / 2));
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            // Drawn for the top edge; the bottom one is the same thing upside down.
            Point At(double x, double y) => new(x, _atBottom ? height - y : y);
            var outward = _atBottom ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;
            var inward = _atBottom ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
            var size = new System.Windows.Size(round, round);
            path.BeginFigure(At(-round, 0), true, false);
            path.ArcTo(At(0, round), size, 0, false, outward, true, true);
            path.LineTo(At(0, height - round), true, true);
            path.ArcTo(At(round, height), size, 0, false, inward, true, true);
            path.LineTo(At(width - round, height), true, true);
            path.ArcTo(At(width, height - round), size, 0, false, inward, true, true);
            path.LineTo(At(width, round), true, true);
            path.ArcTo(At(width + round, 0), size, 0, false, outward, true, true);
        }
        geometry.Freeze();
        var border = BorderThickness.Bottom > 0 && BorderBrush != null ? new System.Windows.Media.Pen(BorderBrush, BorderThickness.Bottom) : null;
        context.DrawGeometry(Background, border, geometry);
    }
}

/// <summary>Fonts for the bar: installed ones, and font files dropped into Belie's own fonts folder.</summary>
internal static class BarFonts
{
    private static readonly Dictionary<string, System.Windows.Media.FontFamily> Private = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _read;

    public static string Folder => Path.Combine(AppPaths.BaseDir, "fonts");

    private static void Refresh()
    {
        try
        {
            if (!Directory.Exists(Folder)) { Private.Clear(); return; }
            var changed = Directory.GetLastWriteTimeUtc(Folder);
            if (changed == _read) return;
            _read = changed;
            Private.Clear();
            foreach (var family in Fonts.GetFontFamilies(new Uri(Folder + Path.DirectorySeparatorChar)))
                foreach (var name in family.FamilyNames.Values) Private[name] = family;
        }
        catch (Exception ex) { Logger.Warn("The fonts folder could not be read: " + ex.Message); }
    }

    /// <summary>The first of a list of fonts that exists, as in a stylesheet. Symbols and emoji come from Windows.</summary>
    public static System.Windows.Media.FontFamily Find(string names)
    {
        Refresh();
        foreach (var name in names.Split(',').Select(name => name.Trim().Trim('"', '\'')))
            if (Private.TryGetValue(name, out var family)) return family;
        var clean = string.Join(", ", names.Split(',').Select(name => name.Trim().Trim('"', '\'')).Where(name => name.Length > 0));
        return new System.Windows.Media.FontFamily((clean.Length > 0 ? clean + ", " : "") + "Segoe UI, Segoe UI Symbol, Segoe MDL2 Assets");
    }

    public static IReadOnlyList<string> Names()
    {
        Refresh();
        return Private.Keys.Concat(Fonts.SystemFontFamilies.Select(family => family.Source)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToArray();
    }
}
