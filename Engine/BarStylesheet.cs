using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point = System.Windows.Point;

namespace WallpaperProfiles.Engine;

internal enum BarShape { None, Island, Notch }

/// <summary>Something on the bar that a stylesheet can select, such as a module inside the left part.</summary>
internal sealed record BarElement(string Tag, string Id = "", string Classes = "", BarElement? Parent = null)
{
    public bool Has(string name) => Classes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(name, StringComparer.OrdinalIgnoreCase);
}

/// <summary>The named colors a style can use: the bar's own, and any the stylesheet defines.</summary>
internal sealed class BarPalette
{
    private readonly Dictionary<string, Color> _colors = new(StringComparer.OrdinalIgnoreCase);

    public BarPalette(Color background, Color text, Color accent, Color border)
    { _colors["background"] = background; _colors["text"] = text; _colors["accent"] = accent; _colors["border"] = border; }

    public Color this[string name] => _colors[name];

    public void Define(IEnumerable<(string Name, string Value)> definitions)
    {
        foreach (var (name, value) in definitions) if (TryColor(value, out var color)) _colors[name] = color;
    }

    /// <summary>Understands #abc, names, rgb(), rgba(), @name, alpha(), shade(), lighter(), darker() and mix().</summary>
    public bool TryColor(string text, out Color color)
    {
        text = text.Trim();
        color = Colors.Transparent;
        if (text.Length == 0) return false;
        if (text[0] == '@') return _colors.TryGetValue(text[1..], out color);
        if (text.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return true;
        var open = text.IndexOf('(');
        if (open > 0 && text.EndsWith(')'))
        {
            var arguments = BarStylesheet.Split(text[(open + 1)..^1], ',');
            var name = text[..open].Trim().ToLowerInvariant();
            switch (name)
            {
                case "rgb" or "rgba":
                    if (arguments.Count == 1) arguments = arguments[0].Replace("/", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                    if (arguments.Count is not (3 or 4)) return false;
                    var parts = new double[4];
                    parts[3] = 1;
                    for (var i = 0; i < arguments.Count; i++)
                    {
                        if (!Number(arguments[i], out parts[i])) return false;
                        if (arguments[i].Trim().EndsWith('%') && i < 3) parts[i] *= 255;
                    }
                    color = Color.FromArgb(Byte(parts[3] * 255), Byte(parts[0]), Byte(parts[1]), Byte(parts[2]));
                    return true;
                case "alpha" when arguments.Count == 2 && TryColor(arguments[0], out color) && Number(arguments[1], out var amount):
                    color.A = Byte(color.A * amount);
                    return true;
                case "shade" when arguments.Count == 2 && TryColor(arguments[0], out color) && Number(arguments[1], out var shade):
                    color = Shade(color, shade);
                    return true;
                case "lighter" or "darker" when arguments.Count == 1 && TryColor(arguments[0], out color):
                    color = Shade(color, name == "lighter" ? 1.3 : .7);
                    return true;
                case "mix" when arguments.Count == 3 && TryColor(arguments[0], out var first) && TryColor(arguments[1], out var second) && Number(arguments[2], out var share):
                    share = Math.Clamp(share, 0, 1);
                    color = Color.FromArgb(Byte(first.A + (second.A - first.A) * share), Byte(first.R + (second.R - first.R) * share),
                        Byte(first.G + (second.G - first.G) * share), Byte(first.B + (second.B - first.B) * share));
                    return true;
            }
            return false;
        }
        if (text[0] == '#')
        {
            var hex = text[1..];
            if (hex.Length is 3 or 4) hex = string.Concat(hex.Select(digit => new string(digit, 2)));
            if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)) return false;
            if (hex.Length == 6) value = value << 8 | 255; // The opacity comes last, as on the web.
            color = Color.FromArgb((byte)value, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8));
            return true;
        }
        if (!text.All(char.IsLetter)) return false;
        try { color = (Color)ColorConverter.ConvertFromString(text); return true; }
        catch (FormatException) { return false; }
    }

    public Brush? TryBrush(string text)
    {
        text = text.Trim();
        Brush brush;
        if (text.StartsWith("linear-gradient(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')'))
        {
            var parts = BarStylesheet.Split(text[16..^1], ',');
            double angle = 180;
            if (parts.Count > 0 && Angle(parts[0]) is { } given) { angle = given; parts.RemoveAt(0); }
            if (parts.Count < 2) return null;
            var stops = new GradientStopCollection();
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i].Trim();
                double offset = i / (parts.Count - 1d);
                var space = part.LastIndexOf(' ');
                if (space > 0 && part.EndsWith('%') && Number(part[(space + 1)..], out var place)) { offset = place; part = part[..space]; }
                if (!TryColor(part, out var stop)) return null;
                stops.Add(new GradientStop(stop, offset));
            }
            // 0 points up and 90 to the right, as on the web.
            var radians = angle * Math.PI / 180;
            Vector direction = new(Math.Sin(radians) / 2, -Math.Cos(radians) / 2);
            brush = new LinearGradientBrush(stops, new Point(.5, .5) - direction, new Point(.5, .5) + direction);
        }
        else
        {
            if (text.Equals("none", StringComparison.OrdinalIgnoreCase)) return Brushes.Transparent;
            if (!TryColor(text, out var color)) return null;
            brush = new SolidColorBrush(color);
        }
        brush.Freeze();
        return brush;
    }

    private static double? Angle(string text) => text.Trim().ToLowerInvariant() switch
    {
        "to right" => 90, "to left" => 270, "to top" => 0, "to bottom" => 180, "to bottom right" => 135, "to top right" => 45, "to bottom left" => 225, "to top left" => 315,
        var value when value.EndsWith("deg") && Number(value[..^3], out var degrees) => degrees,
        _ => null
    };

    internal static bool Number(string text, out double value)
    {
        text = text.Trim();
        var percent = text.EndsWith('%');
        if (!double.TryParse(percent ? text[..^1] : text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !double.IsFinite(value)) return false;
        if (percent) value /= 100;
        return true;
    }

    private static Color Shade(Color color, double by) => Color.FromArgb(color.A, Byte(color.R * by), Byte(color.G * by), Byte(color.B * by));
    private static byte Byte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}

/// <summary>What styles say about one element. Null leaves the bar's own choice.</summary>
internal sealed class BarLook
{
    public Brush? Color, Background, IconColor, BorderColor;
    public Thickness? Border, Padding, Margin;
    public CornerRadius? Radius;
    public string? Font;
    public double? Size, Opacity, Width, MinWidth, Height, Spacing;
    public FontWeight? Weight;
    public bool? Italic, Hidden;
    public BarShape? Shape;

    public static BarLook From(IEnumerable<KeyValuePair<string, string>> declarations, BarPalette palette, double em)
    {
        var look = new BarLook();
        foreach (var (name, value) in declarations) look.Set(name, value, palette, em);
        return look;
    }

    /// <summary>Takes one declaration. False when it means nothing here.</summary>
    internal bool Set(string name, string value, BarPalette palette, double em)
    {
        value = value.Trim();
        if (value.EndsWith("!important", StringComparison.OrdinalIgnoreCase)) value = value[..^10].TrimEnd();
        double? Length(string text) => BarStylesheet.Length(text, em);
        switch (name.ToLowerInvariant())
        {
            case "color": return (Color = palette.TryBrush(value) ?? Color) != null;
            case "background" or "background-color" or "background-image": return (Background = palette.TryBrush(value) ?? Background) != null;
            case "icon-color": return (IconColor = palette.TryBrush(value) ?? IconColor) != null;
            case "border-color": return (BorderColor = palette.TryBrush(value) ?? BorderColor) != null;
            case "border-width": return Sides(value, em) is { } width && (Border = width) != null;
            case "border":
                if (value is "none" or "0") { Border = new Thickness(0); return true; }
                foreach (var part in BarStylesheet.Split(value, ' '))
                {
                    if (Length(part) is { } size) Border = new Thickness(size);
                    else if (palette.TryBrush(part) is { } brush) BorderColor = brush;
                    else if (part is not ("solid" or "none")) return false;
                }
                return true;
            case "border-radius":
                if (Sides(value, em) is not { } round) return false;
                Radius = new CornerRadius(round.Top, round.Right, round.Bottom, round.Left); // Corners come in the order of sides, from the top left.
                return true;
            case "padding": return Sides(value, em) is { } padding && (Padding = padding) != null;
            case "margin": return Sides(value, em) is { } margin && (Margin = margin) != null;
            case "font-family": Font = value; return value.Length > 0;
            case "font-size": return Length(value) is { } font && (Size = Math.Clamp(font, 4, 60)) > 0;
            case "font-weight":
                Weight = value.ToLowerInvariant() switch
                {
                    "bold" or "700" => FontWeights.Bold, "600" => FontWeights.SemiBold, "500" => FontWeights.Medium, "800" or "900" or "bolder" => FontWeights.Black,
                    "300" or "200" or "100" or "lighter" => FontWeights.Light, "normal" or "400" => FontWeights.Normal, _ => Weight
                };
                return Weight != null;
            case "font-style": Italic = value == "italic"; return true;
            case "opacity": return BarPalette.Number(value, out var opacity) && (Opacity = Math.Clamp(opacity, 0, 1)) >= 0;
            case "width": return Length(value) is { } wide && (Width = Math.Max(0, wide)) >= 0;
            case "min-width": return Length(value) is { } least && (MinWidth = Math.Max(0, least)) >= 0;
            case "height" or "min-height": return Length(value) is { } tall && (Height = Math.Max(0, tall)) >= 0;
            case "spacing" or "gap": return Length(value) is { } gap && (Spacing = gap) != null;
            case "display": Hidden = value == "none"; return true;
            case "shape":
                Shape = value.ToLowerInvariant() switch { "none" => BarShape.None, "island" => BarShape.Island, "notch" => BarShape.Notch, _ => Shape };
                return Shape != null;
            // Harmless parts of styles written for other bars.
            case "transition" or "transition-property" or "transition-duration" or "transition-timing-function" or "animation" or "box-shadow" or "text-shadow": return true;
            default: return false;
        }
    }

    private static Thickness? Sides(string value, double em)
    {
        var sizes = BarStylesheet.Split(value, ' ').Select(part => BarStylesheet.Length(part, em)).ToArray();
        if (sizes.Length is < 1 or > 4 || sizes.Any(size => size == null)) return null;
        double top = sizes[0]!.Value, right = sizes.Length > 1 ? sizes[1]!.Value : top, bottom = sizes.Length > 2 ? sizes[2]!.Value : top, left = sizes.Length > 3 ? sizes[3]!.Value : right;
        return new Thickness(left, top, right, bottom);
    }
}

/// <summary>
/// A stylesheet for the bar, in the part of CSS that Waybar users know. Anything it does not understand is reported
/// and skipped, so one mistake does not lose the rest.
/// </summary>
internal sealed class BarStylesheet
{
    private sealed record Part(string Tag, string Id, string[] Classes);
    private sealed record Rule(Part[] Selector, int Weight, List<KeyValuePair<string, string>> Declarations);

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Names other bars use for the same things.
        ["focused"] = "active", ["visible"] = "active", ["persistent"] = "empty", ["modules-left"] = "left", ["modules-center"] = "center", ["modules-right"] = "right"
    };
    private static readonly Dictionary<string, string> Ids = new(StringComparer.OrdinalIgnoreCase)
    {
        ["window"] = "title", ["mpris"] = "media", ["mpd"] = "media", ["pulseaudio"] = "volume", ["wireplumber"] = "volume", ["cava"] = "visualizer"
    };

    private readonly List<Rule> _rules = new();
    public List<(string Name, string Value)> Colors { get; } = new();
    public List<string> Problems { get; } = new();
    public bool IsEmpty => _rules.Count == 0;

    public static BarStylesheet Parse(string text)
    {
        var sheet = new BarStylesheet();
        var check = new BarPalette(System.Windows.Media.Colors.Black, System.Windows.Media.Colors.White, System.Windows.Media.Colors.White, System.Windows.Media.Colors.Gray);
        text = WithoutComments(text);
        var at = 0;
        int Line(int index) => text.AsSpan(0, Math.Min(index, text.Length)).Count('\n') + 1;
        while (true)
        {
            while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
            if (at >= text.Length) break;
            if (text[at] == '@')
            {
                var end = text.IndexOf(';', at);
                if (end < 0) end = text.Length;
                var words = text[(at + 1)..end].Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 3 && words[0] == "define-color")
                {
                    var before = check.TryColor("@" + words[1], out _);
                    check.Define(new[] { (words[1], words[2]) });
                    if (!before && !check.TryColor("@" + words[1], out _)) sheet.Problems.Add($"Line {Line(at)}: {words[2].Trim()} is not a color.");
                    sheet.Colors.Add((words[1], words[2]));
                }
                else sheet.Problems.Add($"Line {Line(at)}: only @define-color is understood.");
                at = end + 1;
                continue;
            }
            var open = text.IndexOf('{', at);
            var close = open < 0 ? -1 : text.IndexOf('}', open);
            if (close < 0) { sheet.Problems.Add($"Line {Line(at)}: {{ or }} is missing."); break; }
            var declarations = new List<KeyValuePair<string, string>>();
            var place = open + 1;
            foreach (var declaration in text[(open + 1)..close].Split(';'))
            {
                var colon = declaration.IndexOf(':');
                if (colon > 0)
                {
                    var name = declaration[..colon].Trim();
                    var value = declaration[(colon + 1)..].Trim();
                    if (new BarLook().Set(name, value, check, 13)) declarations.Add(new(name, value));
                    else sheet.Problems.Add($"Line {Line(place + declaration.Length - declaration.TrimStart().Length)}: {name}: {value} is not understood.");
                }
                else if (declaration.Trim().Length > 0) sheet.Problems.Add($"Line {Line(place)}: {declaration.Trim()} is not understood.");
                place += declaration.Length + 1;
            }
            foreach (var selector in text[at..open].Split(','))
                if (Read(selector) is { Length: > 0 } parts)
                    sheet._rules.Add(new Rule(parts, parts.Sum(part => (part.Id.Length > 0 ? 100 : 0) + part.Classes.Length * 10 + (part.Tag.Length > 0 ? 1 : 0)), declarations));
                else sheet.Problems.Add($"Line {Line(at)}: {selector.Trim()} cannot be selected.");
            at = close + 1;
        }
        // Later rules win over earlier ones that are just as specific.
        var ordered = sheet._rules.OrderBy(rule => rule.Weight).ToArray();
        sheet._rules.Clear(); sheet._rules.AddRange(ordered);
        return sheet;
    }

    public IEnumerable<KeyValuePair<string, string>> For(BarElement element)
        => _rules.Where(rule => Matches(rule.Selector, rule.Selector.Length - 1, element)).SelectMany(rule => rule.Declarations);

    public bool Styles(BarElement element) => _rules.Any(rule => Matches(rule.Selector, rule.Selector.Length - 1, element));

    private static bool Matches(Part[] selector, int index, BarElement element)
    {
        if (!Fits(selector[index], element)) return false;
        if (index == 0) return true;
        for (var parent = element.Parent; parent != null; parent = parent.Parent) if (Matches(selector, index - 1, parent)) return true;
        return false;
    }

    private static bool Fits(Part part, BarElement element) =>
        (part.Tag.Length == 0 || part.Tag.Equals(element.Tag, StringComparison.OrdinalIgnoreCase))
        && (part.Id.Length == 0 || part.Id.Equals(element.Id, StringComparison.OrdinalIgnoreCase)) && part.Classes.All(element.Has);

    private static Part[]? Read(string selector)
    {
        var parts = new List<Part>();
        foreach (var word in selector.Replace(">", " ").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            string tag = "", id = "";
            var classes = new List<string>();
            for (var i = 0; i < word.Length;)
            {
                var mark = word[i] is '#' or '.' or ':' ? word[i++] : ' ';
                var start = i;
                while (i < word.Length && word[i] is not ('#' or '.' or ':')) i++;
                var name = word[start..i];
                if (name.Length == 0 || name != "*" && !name.All(letter => char.IsLetterOrDigit(letter) || letter is '-' or '_')) return null;
                if (mark == '#') id = Ids.GetValueOrDefault(name, name);
                else if (mark is '.' or ':') classes.Add(Aliases.GetValueOrDefault(name, name));
                else if (name != "*") tag = name;
            }
            // Waybar calls the bar itself window#waybar.
            if (tag == "window" || id == "waybar") parts.Add(new Part("bar", "", classes.Where(name => name != "waybar").ToArray()));
            else parts.Add(new Part(tag is "label" ? "" : tag, id, classes.ToArray()));
        }
        return parts.ToArray();
    }

    private static string WithoutComments(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) end = text.Length;
                // Lines are kept so that problems are reported where they are.
                result.Append('\n', text.AsSpan(i, Math.Min(end, text.Length) - i).Count('\n'));
                i = end + 1;
            }
            else result.Append(text[i]);
        }
        return result.ToString();
    }

    /// <summary>Splits at a separator that is not inside brackets.</summary>
    internal static List<string> Split(string text, char separator)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] == '(') depth++;
            else if (i < text.Length && text[i] == ')') depth--;
            else if (i == text.Length || depth == 0 && (text[i] == separator || separator == ' ' && char.IsWhiteSpace(text[i])))
            {
                if (text[start..i].Trim() is { Length: > 0 } part) parts.Add(part);
                start = i + 1;
            }
        }
        return parts;
    }

    internal static double? Length(string text, double em)
    {
        text = text.Trim().ToLowerInvariant();
        var scale = 1d;
        if (text.EndsWith("rem")) { text = text[..^3]; scale = em; }
        else if (text.EndsWith("em")) { text = text[..^2]; scale = em; }
        else if (text.EndsWith("px")) text = text[..^2];
        else if (text.EndsWith("pt")) { text = text[..^2]; scale = 96 / 72d; }
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? Math.Clamp(value * scale, -500, 2000) : null;
    }
}
