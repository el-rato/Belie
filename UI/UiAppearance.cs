using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Color = System.Windows.Media.Color;

namespace WallpaperProfiles.UI;

internal sealed record UiTheme(string Name, string Background, string Surface, string Raised, string Hover,
    string Border, string StrongBorder, string Text, string Secondary, string Muted, string Accent, string Subtle)
{
    public ImageSource Icon => UiAppearance.RenderIcon(this, 32);
}

internal static class UiAppearance
{
    public static IReadOnlyList<UiTheme> Themes { get; } = new[]
    {
        new UiTheme("Sage", "#171918", "#1D201E", "#242825", "#2D332F", "#343B36", "#59645D", "#F1F2EE", "#BBC3BC", "#9AA69D", "#D2E3D6", "#2C3930"),
        new UiTheme("Midnight", "#111725", "#182132", "#202B40", "#2A3852", "#33425D", "#62779A", "#EDF3FF", "#BACAE2", "#8DABC9", "#ABCFFF", "#263D60"),
        new UiTheme("Amethyst", "#1B1523", "#251D30", "#30253E", "#3C2F4D", "#493854", "#80678F", "#F5EDFF", "#D2C1DF", "#B49BC6", "#D5B6F4", "#433052"),
        new UiTheme("Ember", "#211815", "#2B201B", "#372923", "#453129", "#503C31", "#8C6C55", "#FFF2E7", "#DFC7B2", "#BE9E82", "#EFC29B", "#50382A"),
        new UiTheme("Daylight", "#F4F3ED", "#FEFDF8", "#EAECE4", "#E1E6DC", "#CCD4C9", "#8C9E8E", "#202C25", "#4C6053", "#62786A", "#A9C9B4", "#DCE9DF")
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
            ("SignalBrush", theme.Name == "Daylight" ? "#986620" : "#D9AF6E")
        }) resources[key] = new SolidColorBrush(Parse(hex));
        resources["BgColor"] = Parse(theme.Background);
        var accent = Parse(theme.Accent);
        var accents = new[] { accent, Mix(accent, Colors.White, .2), Mix(accent, Colors.Black, .15), Parse(theme.Subtle) };
        var colorKeys = new[] { "SceneAccentColor", "SceneAccentHoverColor", "SceneAccentPressedColor", "SceneAccentSubtleColor" };
        var brushKeys = new[] { "AccentBrush", "AccentHoverBrush", "AccentPressedBrush", "AccentSubtleBrush" };
        for (var i = 0; i < accents.Length; i++) { resources[colorKeys[i]] = accents[i]; resources[brushKeys[i]] = new SolidColorBrush(accents[i]); }
        Changed?.Invoke();
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

    public static void Attach(Window window)
    {
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
        return borderResult == 0;
    }
}
