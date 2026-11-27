using System.Globalization;
using Color = System.Windows.Media.Color;

namespace WallpaperProfiles.Engine;

/// <summary>Reads colours written as #RRGGBB.</summary>
internal static class HexColor
{
    public static bool TryParse(string? value, out Color color)
    {
        color = default;
        var text = value?.Trim() ?? "";
        if (text.Length != 7 || text[0] != '#') return false;
        if (!byte.TryParse(text.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red)
            || !byte.TryParse(text.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green)
            || !byte.TryParse(text.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue)) return false;
        color = Color.FromRgb(red, green, blue);
        return true;
    }
}
