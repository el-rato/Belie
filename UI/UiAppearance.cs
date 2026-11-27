using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Color = System.Windows.Media.Color;
using System.Globalization;
using System.Windows.Data;
using Brushes = System.Windows.Media.Brushes;
using SystemColors = System.Windows.SystemColors;
using Controls = System.Windows.Controls;

namespace WallpaperProfiles.UI;

internal sealed record UiTheme(string Name, string Background, string Surface, string Raised, string Hover,
    string Border, string StrongBorder, string Text, string Secondary, string Muted, string Accent, string Subtle)
{
    public ImageSource Icon => UiAppearance.RenderIcon(this, 32);
}

internal static class UiAppearance
{
    static UiAppearance()
    {
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast) && System.Windows.Application.Current != null)
                System.Windows.Application.Current.Dispatcher.Invoke(() => Apply(Current.Name));
        };
    }
    public static IReadOnlyList<UiTheme> Themes { get; } = new[]
    {
        new UiTheme("Sage", "#111613", "#191F1B", "#232B25", "#303B32", "#364239", "#718E83", "#F3F5EC", "#C0CFC7", "#9AACAA", "#DDF79D", "#303D26"),
        new UiTheme("Midnight", "#111318", "#191D25", "#232A35", "#303A48", "#384354", "#62779A", "#EDF3FF", "#BACAE2", "#9BACBF", "#ABCFFF", "#263447"),
        new UiTheme("Amethyst", "#161318", "#1F1B22", "#2A2430", "#39313F", "#44394C", "#80678F", "#F5EDFF", "#D2C1DF", "#B49BC6", "#D5B6F4", "#3B2D47"),
        new UiTheme("Ember", "#121212", "#191919", "#232222", "#302C29", "#3B3531", "#85766B", "#F5F1ED", "#C8BFB8", "#ABA19A", "#EFC1A0", "#382C24"),
        new UiTheme("Daylight", "#F4F3ED", "#FEFDF8", "#EAECE4", "#E1E6DC", "#CCD4C9", "#8C9E8E", "#202C25", "#4C6053", "#52675A", "#A9C9B4", "#DCE9DF"),
        new UiTheme("Obsidian", "#101010", "#191919", "#242424", "#303030", "#383838", "#808080", "#F3F3F3", "#C8C8C8", "#ABABAB", "#DEDEDE", "#303030"),
        new UiTheme("Nord", "#171D27", "#202835", "#2A3443", "#344152", "#3E4B5E", "#7A92AC", "#ECEFF4", "#C6D2E0", "#AEBED0", "#88C0D0", "#293E48"),
        new UiTheme("Dracula", "#181720", "#211F2C", "#2C293A", "#373345", "#443D55", "#9380AB", "#F8F8F2", "#D4CDE3", "#B7ABC9", "#BD93F9", "#3A2B4F"),
        new UiTheme("Rose", "#191315", "#231B1F", "#30252B", "#3D3037", "#4C3A43", "#A17E8F", "#FFF0F4", "#DEC6D1", "#BFA4B1", "#F2ADCA", "#472C39"),
        new UiTheme("Ocean", "#0E181B", "#152226", "#1F2E33", "#293C42", "#334A50", "#71979F", "#EDF8F7", "#BDD7D8", "#9BBABD", "#79D5C8", "#213E3B"),
        new UiTheme("Mocha", "#191410", "#241E18", "#302820", "#3E3329", "#4D4033", "#A18A70", "#F8F0E4", "#DDCBB6", "#BDA88F", "#DEB77C", "#453521"),
        new UiTheme("Tokyo", "#141522", "#1C1E30", "#272A40", "#333751", "#414660", "#858EB8", "#EEF0FF", "#C9CDEE", "#ADB3D8", "#AFA0FF", "#342D50"),
        new UiTheme("Sunset", "#1B1314", "#271C1D", "#342729", "#423335", "#524044", "#AB848A", "#FFF1E9", "#E4C6BE", "#C6A59E", "#FFA58C", "#4C302A")
    };
    public static UiTheme Current { get; private set; } = Themes[0];
    public static event Action? Changed;

    public static UiTheme Find(string? name) => Themes.FirstOrDefault(theme => theme.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];

    public static void Apply(string? name)
    {
        Current = Find(name);
        var resources = FindPalette(System.Windows.Application.Current.Resources);
        var theme = Current;
        foreach (var (key, hex) in new[]
        {
            ("BgBrush", theme.Background), ("SurfaceBrush", theme.Surface), ("SurfaceRaisedBrush", theme.Raised),
            ("SurfaceHoverBrush", theme.Hover), ("BorderBrush", theme.Border), ("BorderStrongBrush", theme.StrongBorder),
            ("TextPrimaryBrush", theme.Text), ("TextSecondaryBrush", theme.Secondary), ("TextTertiaryBrush", theme.Muted),
            ("DangerBrush", theme.Name == "Daylight" ? "#AD4040" : "#E49B94"),
            ("GoodBrush", theme.Name == "Daylight" ? "#34724B" : "#98CAA5"),
            ("SignalBrush", theme.Name == "Daylight" ? "#80551B" : "#D9AF6E")
        }) resources[key] = new SolidColorBrush(Parse(hex));
        resources["BgColor"] = Parse(theme.Background);
        var accent = Parse(theme.Accent);
        var accents = new[] { accent, Mix(accent, Colors.White, .2), Mix(accent, Colors.Black, .15), Parse(theme.Subtle) };
        var colorKeys = new[] { "SceneAccentColor", "SceneAccentHoverColor", "SceneAccentPressedColor", "SceneAccentSubtleColor" };
        var brushKeys = new[] { "AccentBrush", "AccentHoverBrush", "AccentPressedBrush", "AccentSubtleBrush" };
        for (var i = 0; i < accents.Length; i++) { resources[colorKeys[i]] = accents[i]; resources[brushKeys[i]] = new SolidColorBrush(accents[i]); }
        resources["AccentTextBrush"] = ReadableAccent(accent);
        ApplyGlass(resources, theme, accent);
        if (SystemParameters.HighContrast)
        {
            foreach (var key in new[] { "BgBrush", "SurfaceBrush", "SurfaceRaisedBrush", "SurfaceHoverBrush", "AccentSubtleBrush" }) resources[key] = SystemColors.WindowBrush;
            foreach (var key in new[] { "TextPrimaryBrush", "TextSecondaryBrush", "TextTertiaryBrush", "BorderBrush", "BorderStrongBrush", "DangerBrush", "GoodBrush", "SignalBrush" }) resources[key] = SystemColors.WindowTextBrush;
            foreach (var key in new[] { "AccentBrush", "AccentHoverBrush", "AccentPressedBrush", "AccentTextBrush" }) resources[key] = SystemColors.HighlightBrush;
            resources["BgColor"] = SystemColors.WindowColor;
            foreach (var key in new[] { "BackdropBrush", "PaneBrush", "PaneBaseBrush", "CardBrush", "CardHoverBrush" }) resources[key] = SystemColors.WindowBrush;
            resources["PaneEdgeBrush"] = resources["CardEdgeBrush"] = SystemColors.WindowTextBrush;
            resources["PaneGlowBrush"] = SystemColors.HighlightBrush;
        }
        Changed?.Invoke();
    }

    /// <summary>The glass strength the person chose; <see cref="EffectiveGlass"/> says what this machine can show.</summary>
    public static Persistence.GlassLevel Glass { get; set; } = Persistence.GlassLevel.Strong;

    /// <summary>Glass needs Windows 11's backdrop materials, transparency effects turned on, and no high contrast.</summary>
    public static Persistence.GlassLevel EffectiveGlass => GlassSupported ? Glass : Persistence.GlassLevel.Off;

    public static bool GlassSupported => BlurAvailable && !SystemParameters.HighContrast && TransparencyEnabled();

    /// <summary>The compositor's blur behind a window arrived in Windows 10 version 1803.</summary>
    private static bool BlurAvailable => Environment.OSVersion.Version.Build >= 17134;

    private static bool TransparencyEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int value || value != 0;
        }
        catch (Exception) { return true; }
    }

    private static void ApplyGlass(ResourceDictionary resources, UiTheme theme, Color accent)
    {
        var level = EffectiveGlass;
        var background = Parse(theme.Background);
        var surface = Parse(theme.Surface);
        var (backdrop, pane) = level switch
        {
            Persistence.GlassLevel.Strong => (.16, .46),
            Persistence.GlassLevel.Light => (.36, .64),
            _ => (1d, 1d)
        };
        resources["BackdropBrush"] = Frozen(new SolidColorBrush(WithAlpha(background, backdrop)));
        // Off keeps today's look: panes share the window colour and only their edges show.
        resources["PaneBrush"] = Frozen(new SolidColorBrush(level == Persistence.GlassLevel.Off ? background : WithAlpha(surface, pane)));
        resources["PaneBaseBrush"] = level == Persistence.GlassLevel.Off ? Frozen(new SolidColorBrush(background)) : Brushes.Transparent;
        var edge = Parse(theme.Border);
        resources["PaneEdgeBrush"] = Frozen(new SolidColorBrush(level == Persistence.GlassLevel.Off ? edge : WithAlpha(Mix(edge, Colors.White, .12), .75)));
        // Cards inside a pane lighten the glass a little instead of covering it.
        var light = theme.Name == "Daylight";
        resources["CardBrush"] = Frozen(new SolidColorBrush(level == Persistence.GlassLevel.Off ? surface : WithAlpha(Colors.White, light ? .45 : .055)));
        resources["CardHoverBrush"] = Frozen(new SolidColorBrush(level == Persistence.GlassLevel.Off ? Parse(theme.Hover) : WithAlpha(Colors.White, light ? .65 : .10)));
        resources["CardEdgeBrush"] = Frozen(new SolidColorBrush(level == Persistence.GlassLevel.Off ? edge : WithAlpha(light ? Colors.Black : Colors.White, light ? .08 : .09)));
        resources["PaneGlowBrush"] = Frozen(new LinearGradientBrush(new GradientStopCollection
        {
            new(accent, 0), new(Mix(accent, Colors.White, .25), .5), new(ShiftHue(accent, 48), 1)
        }, new System.Windows.Point(0, 0), new System.Windows.Point(1, 1)));
    }

    private static T Frozen<T>(T brush) where T : Freezable { brush.Freeze(); return brush; }
    private static Color WithAlpha(Color color, double alpha) => Color.FromArgb((byte)Math.Round(alpha * 255), color.R, color.G, color.B);

    /// <summary>The accent turned around the colour wheel, so the focus edge runs between two related colours.</summary>
    internal static Color ShiftHue(Color color, double degrees)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        double hue = delta == 0 ? 0 : max == r ? 60 * (((g - b) / delta) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        double saturation = max == 0 ? 0 : delta / max;
        hue = ((hue + degrees) % 360 + 360) % 360;
        double c = max * saturation, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = max - c;
        var (r1, g1, b1) = (int)(hue / 60) switch { 0 => (c, x, 0d), 1 => (x, c, 0d), 2 => (0d, c, x), 3 => (0d, x, c), 4 => (x, 0d, c), _ => (c, 0d, x) };
        return Color.FromRgb((byte)Math.Round((r1 + m) * 255), (byte)Math.Round((g1 + m) * 255), (byte)Math.Round((b1 + m) * 255));
    }

    internal static System.Windows.Media.Brush ReadableAccent(Color accent)
    {
        var surfaces = new[] { Current.Background, Current.Surface, Current.Raised, Current.Subtle }.Select(Parse).ToArray();
        var target = Current.Name == "Daylight" ? Colors.Black : Colors.White;
        for (var step = 0; step <= 20; step++)
        {
            var candidate = Mix(accent, target, step / 20d);
            var luminance = AccentInkConverter.Luminance(candidate);
            if (surfaces.All(surface => (Math.Max(luminance, AccentInkConverter.Luminance(surface)) + .05)
                / (Math.Min(luminance, AccentInkConverter.Luminance(surface)) + .05) >= 4.5))
                return new SolidColorBrush(candidate);
        }
        return new SolidColorBrush(target);
    }

    private static ResourceDictionary FindPalette(ResourceDictionary resources)
    {
        if (resources.Keys.Cast<object>().Contains("BgBrush")) return resources;
        foreach (var merged in resources.MergedDictionaries.Reverse())
            if (merged.Contains("BgBrush")) return FindPalette(merged);
        throw new InvalidOperationException("The UI palette is unavailable.");
    }
    private static Color Parse(string hex) => (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
    private static Color Mix(Color color, Color target, double amount) => Color.FromRgb(
        (byte)(color.R * (1 - amount) + target.R * amount), (byte)(color.G * (1 - amount) + target.G * amount), (byte)(color.B * (1 - amount) + target.B * amount));

    public static BitmapSource RenderIcon(UiTheme theme, int size)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.PushTransform(new ScaleTransform(size / 256d, size / 256d));
            var accent = Parse(theme.Accent);
            var tile = new LinearGradientBrush(Mix(accent, Colors.White, .12), Mix(accent, Colors.Black, .16), 65);
            drawing.DrawRoundedRectangle(tile, null, new Rect(8, 8, 240, 240), 62, 62);
            // Open counters keep the Belie monogram readable at tray sizes.
            var mark = Geometry.Parse("M 70,58 L 137,58 C 180,58 195,78 195,101 C 195,117 186,126 174,132 C 191,138 201,150 201,166 C 201,189 182,203 141,203 L 70,203 Z M 104,87 L 104,118 L 137,118 C 154,118 162,113 162,102 C 162,92 154,87 137,87 Z M 104,147 L 104,174 L 141,174 C 159,174 168,170 168,161 C 168,152 159,147 141,147 Z");
            var ink = Parse(theme.Name == "Daylight" ? "#244333" : theme.Background);
            drawing.DrawGeometry(new SolidColorBrush(ink), null, mark);
            drawing.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }

    public static byte[] IconBytes(UiTheme theme, params int[] sizes)
    {
        var frames = sizes.Select(size =>
        {
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(RenderIcon(theme, size)));
            using var image = new MemoryStream(); png.Save(image); return image.ToArray();
        }).ToArray();
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
        var offset = 6 + sizes.Length * 16;
        for (var i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
        }
        foreach (var frame in frames) writer.Write(frame);
        return stream.ToArray();
    }
    public static System.Drawing.Icon CreateTrayIcon()
    {
        using var stream = new MemoryStream(IconBytes(Current, 32));
        using var icon = new System.Drawing.Icon(stream);
        return (System.Drawing.Icon)icon.Clone();
    }

    /// <param name="glass">True for a dialog that floats as blurred glass, like the settings window.</param>
    public static void SetDialogContent(Window dialog, UIElement content, string title, bool glass = false)
    {
        dialog.WindowStyle = WindowStyle.None;
        dialog.BorderThickness = new Thickness(1);
        dialog.UseLayoutRounding = true;
        dialog.SetResourceReference(Window.FontFamilyProperty, "AppFont");
        dialog.SetResourceReference(Window.BackgroundProperty, glass ? "PaneBrush" : "BgBrush");
        dialog.SetResourceReference(Window.ForegroundProperty, "TextPrimaryBrush");
        dialog.SetResourceReference(Window.BorderBrushProperty, "BorderBrush");
        var layout = new Controls.Grid();
        layout.RowDefinitions.Add(new Controls.RowDefinition { Height = new GridLength(48) });
        layout.RowDefinitions.Add(new Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Controls.Border { BorderThickness = new Thickness(0, 0, 0, 1) };
        if (glass) header.Background = Brushes.Transparent;
        else header.SetResourceReference(Controls.Border.BackgroundProperty, "SurfaceBrush");
        header.SetResourceReference(Controls.Border.BorderBrushProperty, "BorderBrush");
        header.MouseLeftButtonDown += (_, args) =>
        {
            if (args.LeftButton == System.Windows.Input.MouseButtonState.Pressed) dialog.DragMove();
        };
        var headerContent = new Controls.DockPanel();
        var close = new Controls.Button
        {
            Content = "\uE8BB", Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center, ToolTip = "Close", Focusable = true
        };
        close.SetResourceReference(FrameworkElement.StyleProperty, "TitleBarCloseButton");
        System.Windows.Automation.AutomationProperties.SetName(close, "Close " + title.ToLowerInvariant());
        close.Click += (_, _) => dialog.Close();
        Controls.DockPanel.SetDock(close, Controls.Dock.Right);
        headerContent.Children.Add(close);
        var label = new Controls.StackPanel { Orientation = Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0), IsHitTestVisible = false };
        var icon = new Controls.Image { Width = 18, Height = 18 };
        icon.SetBinding(Controls.Image.SourceProperty, new System.Windows.Data.Binding(nameof(Window.Icon)) { Source = dialog });
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        label.Children.Add(icon);
        label.Children.Add(new Controls.TextBlock { Text = title, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
        headerContent.Children.Add(label);
        header.Child = headerContent;
        layout.Children.Add(header);
        Controls.Grid.SetRow(content, 1);
        layout.Children.Add(content);
        dialog.Content = layout;
        Attach(dialog, glass);
    }

    private static readonly DependencyProperty GlassHostProperty = DependencyProperty.RegisterAttached("GlassHost", typeof(bool), typeof(UiAppearance));

    /// <param name="glass">True for a window whose background may show the blurred desktop behind it.</param>
    public static void Attach(Window window, bool glass = false)
    {
        window.SetValue(GlassHostProperty, glass);
        // Windows only blurs what is behind a window whose pixels may be see-through (a layered window).
        // That has to be decided before the window first shows, so glass can then be turned off and on freely.
        if (glass && BlurAvailable && !window.IsLoaded && new WindowInteropHelper(window).Handle == IntPtr.Zero)
            window.AllowsTransparency = true;
        WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        void Update() { window.Icon = RenderIcon(Current, 64); UpdateFrame(window); }
        Update(); Changed += Update;
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            var source = HwndSource.FromHwnd(handle);
            IntPtr Constrain(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
                => ConstrainMaximizedBounds(window, hwnd, message, wParam, lParam, ref handled);
            source.AddHook(Constrain);
            var work = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
            var size = source.CompositionTarget.TransformFromDevice.Transform(new Vector(work.Width, work.Height));
            window.MinWidth = Math.Min(window.MinWidth, size.X);
            window.MinHeight = Math.Min(window.MinHeight, size.Y);
            window.Width = Math.Min(window.Width, size.X);
            window.Height = Math.Min(window.Height, size.Y);
            UpdateFrame(window);
        };
        window.Closed += (_, _) => Changed -= Update;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo
    {
        public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }
    private static IntPtr ConstrainMaximizedBounds(Window window, IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0024) return IntPtr.Zero; // WM_GETMINMAXINFO
        var screen = System.Windows.Forms.Screen.FromHandle(handle);
        var work = screen.WorkingArea;
        var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        info.MaxPosition = new NativePoint { X = work.Left - screen.Bounds.Left, Y = work.Top - screen.Bounds.Top };
        info.MaxSize = new NativePoint { X = work.Width, Y = work.Height };
        var dpi = VisualTreeHelper.GetDpi(window);
        info.MinTrackSize = new NativePoint { X = Math.Min(work.Width, (int)Math.Ceiling(window.MinWidth * dpi.DpiScaleX)),
            Y = Math.Min(work.Height, (int)Math.Ceiling(window.MinHeight * dpi.DpiScaleY)) };
        Marshal.StructureToPtr(info, lParam, false);
        handled = true;
        return IntPtr.Zero;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)] private struct AccentPolicy { public int State, Flags, Color, Animation; }
    [StructLayout(LayoutKind.Sequential)] private struct CompositionData { public int Attribute; public IntPtr Data; public int Size; }
    [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr window, ref CompositionData data);

    private static void SetBlur(IntPtr handle, bool on)
    {
        var accent = new AccentPolicy { State = on ? 3 : 0 }; // ACCENT_ENABLE_BLURBEHIND, or off.
        var size = Marshal.SizeOf<AccentPolicy>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, pointer, false);
            var data = new CompositionData { Attribute = 19, Data = pointer, Size = size }; // WCA_ACCENT_POLICY
            SetWindowCompositionAttribute(handle, ref data);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static void UpdateGlass(Window window, IntPtr handle)
    {
        // The compositor's blur stays on when the window loses focus, unlike Windows 11's acrylic material,
        // and Belie's own glass brushes tint it. It only shows through a layered window.
        SetBlur(handle, window.AllowsTransparency && EffectiveGlass != Persistence.GlassLevel.Off);
    }
    internal static bool UpdateFrame(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return false;
        var dark = Current.Name == "Daylight" ? 0 : 1;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        var border = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE removes the system's bright outline.
        var borderResult = DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
        var background = Parse(Current.Surface);
        var caption = background.R | (background.G << 8) | (background.B << 16);
        DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        if ((bool)window.GetValue(GlassHostProperty)) UpdateGlass(window, handle);
        return borderResult == 0;
    }
}

internal sealed class AccentInkConverter : IValueConverter
{
    internal static double Luminance(Color color)
    {
        static double Channel(byte value) { var c = value / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
    }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is SolidColorBrush brush && Luminance(brush.Color) < .179 ? Brushes.White : Brushes.Black;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
