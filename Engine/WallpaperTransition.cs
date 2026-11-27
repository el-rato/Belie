using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.UI;

namespace WallpaperProfiles.Engine;

/// <summary>
/// Plays the change from one wallpaper picture to the next in windows just above the desktop, below every app and
/// widget. The real wallpaper is replaced underneath, so nothing is left behind when the windows go away.
/// </summary>
internal sealed class WallpaperTransition : IDisposable
{
    public const string WindowTitle = "Belie wallpaper transition";
    private const double FadeTime = 130;
    // Whole screens are painted, so more than this would cost fast displays a lot and show very little.
    private const double FrameTime = 1000 / 75d;

    private sealed class Scene : IDisposable
    {
        public required Rectangle Bounds;
        public required Bitmap Old, New;
        public required TransitionField? Field;
        public required Color Glow, Line;
        public required TransitionOptions Options;
        public void Dispose() { Old.Dispose(); New.Dispose(); }
    }

    private sealed class Change
    {
        public required List<Scene> Scenes;
        public required Func<Task<bool>> Apply;
        public bool Started;
        public readonly TaskCompletionSource<bool> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly object _gate = new();
    private Change? _next;
    private bool _playing, _disposed;
    private int _request;

    /// <summary>Frames drawn and time taken by the last transition.</summary>
    internal (int Frames, double Milliseconds) LastRun { get; private set; }
    internal bool EvenWhenCovered { get; init; }
    internal bool IsPlaying { get { lock (_gate) return _playing; } }

    /// <summary>
    /// Changes the wallpaper with a transition, or plainly when a transition cannot be shown.
    /// Completes when the wallpaper itself has been set, while the transition may still be playing.
    /// </summary>
    public async Task<bool> ChangeAsync(string path, FitMode fit, TransitionOptions options, Func<Task<bool>> apply)
    {
        if (_disposed || options.Style == WallpaperTransitionStyle.None || !System.Windows.SystemParameters.ClientAreaAnimation
            || System.Windows.SystemParameters.HighContrast || System.Windows.SystemParameters.IsRemoteSession || !EvenWhenCovered && (DesktopLayer.IsDesktopCovered() || DesktopLayer.IsDesktopCoveredTogether())) return await apply();
        var request = Interlocked.Increment(ref _request);
        List<Scene>? scenes = null;
        try { scenes = await Prepare(() => Load(path, fit, options)); }
        catch (Exception ex) { Logger.Warn("The wallpaper transition could not be prepared: " + ex.Message); }
        if (scenes == null) return await apply();
        var change = new Change { Scenes = scenes, Apply = apply };
        Change? replaced = null;
        var start = false;
        lock (_gate)
        {
            if (_disposed || request != _request) change = null;
            else
            {
                replaced = _next;
                _next = change;
                start = !_playing;
                _playing = true;
            }
        }
        // A newer change is on its way; this one is only passed on so it can report that it was superseded.
        if (change == null) { Dispose(scenes); return await apply(); }
        if (replaced != null) { Dispose(replaced.Scenes); replaced.Done.TrySetResult(false); }
        if (start)
        {
            // Its windows sit among Explorer's, so they get a thread that never waits on anything else.
            var thread = new Thread(Play) { IsBackground = true, Name = "Wallpaper transition" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        return await change.Done.Task;
    }

    /// <summary>Reading and planning take a moment of hard work, which must not slow the apps on screen.</summary>
    private static Task<List<Scene>?> Prepare(Func<List<Scene>?> load)
    {
        var done = new TaskCompletionSource<List<Scene>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        new Thread(() => { try { done.SetResult(load()); } catch (Exception ex) { done.SetException(ex); } })
        { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Wallpaper transition planning" }.Start();
        return done.Task;
    }

    private static void Dispose(IEnumerable<Scene> scenes) { foreach (var scene in scenes) scene.Dispose(); }

    private static List<Scene>? Load(string path, FitMode fit, TransitionOptions options)
    {
        var current = CurrentWallpaper();
        if (!File.Exists(current.Path)) return null;
        var bytes = File.ReadAllBytes(path);
        var oldBytes = File.ReadAllBytes(current.Path);
        if (fit == current.Fit && bytes.AsSpan().SequenceEqual(oldBytes)) return null; // Nothing would change.
        using var image = Decode(bytes);
        using var old = Decode(oldBytes);
        if (image == null || old == null) return null;
        var scenes = new List<Scene>();
        try
        {
            options = options with { Style = TransitionField.Resolve(options.Style, Random.Shared) };
            var screens = System.Windows.Forms.Screen.AllScreens.Select(screen => screen.Bounds).ToArray();
            var all = screens.Aggregate(Rectangle.Union);
            var map = fit == FitMode.Tile ? null : SubjectModel.Cached(path);
            if (map == null) SubjectModel.Warm(new[] { path }); // Ready for the next time.
            foreach (var screen in screens)
            {
                var next = Render(image, fit, screen, all);
                (byte[], RectangleF)? cutout = null;
                if (map != null)
                {
                    var place = Place(image.Size, fit, screen, all);
                    cutout = (map, new RectangleF((place.X - screen.X) / (float)screen.Width, (place.Y - screen.Y) / (float)screen.Height,
                        place.Width / (float)screen.Width, place.Height / (float)screen.Height));
                }
                var (field, glow, line, sized) = Plan(next, options, Random.Shared, cutout);
                scenes.Add(new Scene { Bounds = screen, New = next, Old = Render(old, current.Fit, screen, all), Field = field, Glow = glow, Line = line, Options = sized });
            }
            return scenes;
        }
        catch { Dispose(scenes); throw; }
    }

    /// <summary>What a transition to a picture will look like.</summary>
    /// <param name="cutout">The picture's saved cut-out, and where the whole picture lands as fractions of <paramref name="next"/>.</param>
    internal static (TransitionField? Field, Color Glow, Color Line, TransitionOptions Options) Plan(Bitmap next, TransitionOptions options, Random random,
        (byte[] Map, RectangleF Place)? cutout = null)
    {
        // Tiny squares on a huge screen would take longer to plan than to play.
        var cell = Math.Max(Math.Max(1, options.Cell), (int)Math.Ceiling(Math.Sqrt(next.Width * (double)next.Height / 1_000_000)));
        int columns = (next.Width + cell - 1) / cell, rows = (next.Height + cell - 1) / cell;
        var (small, width, height) = Sample(next);
        var field = TransitionField.For(options, columns, rows, () =>
            ((cutout is { } cut ? SubjectFinder.FromCutout(cut.Map, cut.Place, width, height) : null) ?? SubjectFinder.Find(small, width, height)) is { } mask
            ? SubjectFinder.Scale(mask, width, height, columns, rows) : null, random);
        var glow = HexColor.TryParse(options.GlowColor, out var chosen) ? Color.FromArgb(chosen.R, chosen.G, chosen.B) : Vivid(small);
        return (field, glow, Lightest(small), options with { Cell = cell });
    }

    internal static (string Path, FitMode Fit) CurrentWallpaper()
    {
        var path = new System.Text.StringBuilder(1024);
        SystemParametersInfo(0x73 /* SPI_GETDESKWALLPAPER */, (uint)path.Capacity, path, 0);
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        var style = (key?.GetValue("WallpaperStyle") as string ?? "10", key?.GetValue("TileWallpaper") as string ?? "0");
        return (path.ToString(), Enum.GetValues<FitMode>().FirstOrDefault(mode => mode.ToRegistryValues() == style, FitMode.Fill));
    }

    private static Bitmap? Decode(byte[] bytes)
    {
        // The stream has to outlive the picture, so the picture is copied out of it.
        using var stream = new MemoryStream(bytes);
        using var image = Image.FromStream(stream);
        // Turned photos are rare, and guessing how Windows turns them would show as a jump.
        if (image.PropertyIdList.Contains(0x112) && image.GetPropertyItem(0x112)?.Value is [not (0 or 1), ..]) return null;
        var copy = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppRgb);
        using var graphics = Graphics.FromImage(copy);
        graphics.Clear(Color.Black);
        graphics.DrawImage(image, new Rectangle(0, 0, copy.Width, copy.Height));
        return copy;
    }

    /// <summary>Where Windows draws a picture on one screen.</summary>
    internal static Rectangle Place(Size image, FitMode fit, Rectangle screen, Rectangle all)
    {
        if (fit == FitMode.Span) screen = all;
        if (fit is FitMode.Stretch) return screen;
        if (fit is FitMode.Center or FitMode.Tile)
            return new Rectangle(screen.X + (screen.Width - image.Width) / 2, screen.Y + (screen.Height - image.Height) / 2, image.Width, image.Height);
        double across = screen.Width / (double)image.Width, down = screen.Height / (double)image.Height;
        var scale = fit == FitMode.Fit ? Math.Min(across, down) : Math.Max(across, down);
        int width = (int)Math.Round(image.Width * scale), height = (int)Math.Round(image.Height * scale);
        return new Rectangle(screen.X + (screen.Width - width) / 2, screen.Y + (screen.Height - height) / 2, width, height);
    }

    private static Bitmap Render(Bitmap image, FitMode fit, Rectangle screen, Rectangle all)
    {
        var result = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppRgb);
        using var graphics = Graphics.FromImage(result);
        graphics.Clear(SystemColors.Desktop);
        graphics.TranslateTransform(-screen.X, -screen.Y);
        if (fit == FitMode.Tile)
        {
            using var brush = new TextureBrush(image, WrapMode.Tile);
            graphics.FillRectangle(brush, screen);
            return result;
        }
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var edges = new ImageAttributes();
        edges.SetWrapMode(WrapMode.TileFlipXY); // Otherwise the picture's border blends with the background.
        graphics.DrawImage(image, Place(image.Size, fit, screen, all), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, edges);
        return result;
    }

    private static (int[] Pixels, int Width, int Height) Sample(Bitmap bitmap)
    {
        var width = 192;
        var height = Math.Max(18, bitmap.Height * width / bitmap.Width);
        using var small = new Bitmap(width, height, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(small))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(bitmap, 0, 0, width, height);
        }
        var data = small.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        var pixels = new int[width * height];
        Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        small.UnlockBits(data);
        return (pixels.Select(pixel => pixel & 0xFFFFFF).ToArray(), width, height);
    }

    /// <summary>The picture's strongest color, bright enough to glow.</summary>
    internal static Color Vivid(int[] pixels)
    {
        var weights = new double[24]; var sums = new double[24, 3];
        foreach (var pixel in pixels)
        {
            var color = Color.FromArgb(pixel >> 16 & 255, pixel >> 8 & 255, pixel & 255);
            double high = Math.Max(color.R, Math.Max(color.G, color.B)), low = Math.Min(color.R, Math.Min(color.G, color.B));
            var weight = high <= 0 ? 0 : (high - low) / high * (high / 255) * ((high - low) / 255);
            var hue = Math.Min(23, (int)(color.GetHue() / 15));
            weights[hue] += weight;
            sums[hue, 0] += color.R * weight; sums[hue, 1] += color.G * weight; sums[hue, 2] += color.B * weight;
        }
        var best = Array.IndexOf(weights, weights.Max());
        if (weights[best] < pixels.Length * .0008) return Lightest(pixels);
        var parts = Enumerable.Range(0, 3).Select(channel => sums[best, channel] / weights[best]).ToArray();
        var lift = Math.Max(1, 235 / Math.Max(1, parts.Max()));
        return Color.FromArgb((int)Math.Min(255, parts[0] * lift), (int)Math.Min(255, parts[1] * lift), (int)Math.Min(255, parts[2] * lift));
    }

    internal static Color Lightest(int[] pixels)
    {
        var light = pixels.OrderByDescending(pixel => (pixel >> 16 & 255) * 3 + (pixel >> 8 & 255) * 6 + (pixel & 255)).Take(Math.Max(1, pixels.Length / 50)).ToArray();
        return Color.FromArgb((int)light.Average(pixel => pixel >> 16 & 255), (int)light.Average(pixel => pixel >> 8 & 255), (int)light.Average(pixel => pixel & 255));
    }

    private void Play()
    {
        var windows = new List<Surface>();
        Change? change = null, missed = null;
        try
        {
            while (true)
            {
                lock (_gate) { change = _next; _next = null; if (change == null) { _playing = false; return; } }
                Run(change, windows);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("The wallpaper transition failed.", ex);
            lock (_gate) { _playing = false; missed = _next; _next = null; }
        }
        finally
        {
            foreach (var window in windows) window.Dispose();
            // The wallpaper has to change even when its transition cannot be shown.
            foreach (var left in new[] { change, missed })
            {
                if (left == null) continue;
                Dispose(left.Scenes);
                if (!left.Started) _ = Finish(left);
            }
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false); // Whole pictures were read for this.
        }
    }

    private static async Task Finish(Change change)
    {
        try { change.Done.TrySetResult(await change.Apply()); }
        catch (Exception ex) { change.Done.TrySetException(ex); }
    }

    private void Run(Change change, List<Surface> windows)
    {
        try
        {
            if (windows.Count > 0 && !windows.Select(window => window.Bounds).SequenceEqual(change.Scenes.Select(scene => scene.Bounds)))
            {
                foreach (var window in windows) window.Dispose(); // The displays changed.
                windows.Clear();
            }
            var fresh = windows.Count == 0;
            for (var i = 0; i < change.Scenes.Count; i++)
            {
                if (fresh) windows.Add(new Surface(change.Scenes[i].Bounds, change.Scenes[i].Old));
                // Already on screen: it carries on from whatever it shows now.
                windows[i].Begin(change.Scenes[i]);
            }
            // Drawing the outline takes part of the time, so it gets a little more.
            var length = change.Scenes.Max(scene => scene.Options.Seconds * 1000 * (scene.Field?.Lines.Count > 0 ? 1.1 : 1));
            var clock = Stopwatch.StartNew();
            var frames = 0;
            var drawn = double.NegativeInfinity;
            Task<bool>? apply = null;
            while (true)
            {
                System.Windows.Forms.Application.DoEvents();
                var time = clock.Elapsed.TotalMilliseconds;
                // Until it covers the desktop, the window shows the old picture above the same old picture.
                var alpha = fresh ? (byte)Math.Round(255 * Math.Min(1, time / FadeTime)) : (byte)255;
                var progress = (float)Math.Min(1, time / length);
                if (time - drawn >= FrameTime - 2 || progress >= 1 || alpha == 255 && apply == null)
                {
                    foreach (var window in windows) { window.SetAlpha(alpha); window.Draw(progress); }
                    frames++; drawn = time;
                }
                if (apply == null && alpha == 255) { change.Started = true; apply = change.Apply(); _ = Report(change, apply); }
                if (apply is { IsCompleted: true } && (!apply.IsCompletedSuccessfully || !apply.Result)) break; // Still the old wallpaper.
                // Explorer takes a moment to paint the new wallpaper, sometimes with a fade of its own.
                if (progress >= 1 && apply is { IsCompleted: true }) break;
                lock (_gate) if (_next != null && apply is { IsCompleted: true }) break;
                if (_disposed || time > length + 10000) break;
                if (NativeMethods.DwmFlush() != 0) Thread.Sleep(10);
            }
            LastRun = (frames, clock.Elapsed.TotalMilliseconds);
            lock (_gate) if (_next != null) return; // The windows stay for the change that comes next.
            var fade = Stopwatch.StartNew();
            while (fade.Elapsed.TotalMilliseconds < FadeTime && !_disposed)
            {
                System.Windows.Forms.Application.DoEvents();
                var left = 1 - fade.Elapsed.TotalMilliseconds / FadeTime;
                foreach (var window in windows) window.SetAlpha((byte)Math.Round(255 * Math.Max(0, left)));
                if (NativeMethods.DwmFlush() != 0) Thread.Sleep(10);
            }
            foreach (var window in windows) window.Dispose();
            windows.Clear();
        }
        finally
        {
            foreach (var window in windows) window.Detach();
            Dispose(change.Scenes);
        }
    }

    private static async Task Report(Change change, Task<bool> apply)
    {
        try { change.Done.TrySetResult(await apply); }
        catch (Exception ex) { change.Done.TrySetException(ex); }
    }

    public void Dispose()
    {
        Change? waiting;
        lock (_gate) { _disposed = true; waiting = _next; _next = null; }
        if (waiting == null) return;
        Dispose(waiting.Scenes);
        waiting.Done.TrySetResult(false);
    }

    /// <summary>One screen's window and the picture it shows.</summary>
    private sealed unsafe class Surface : System.Windows.Forms.NativeWindow, IDisposable
    {
        private readonly TransitionPainter _painter;
        private readonly IntPtr _memory, _bitmap, _previous;
        private int _alpha = -1;
        private bool _disposed;

        public Rectangle Bounds { get; }

        public Surface(Rectangle bounds, Bitmap picture)
        {
            Bounds = bounds;
            var header = new BITMAPINFOHEADER { Size = sizeof(BITMAPINFOHEADER), Width = bounds.Width, Height = -bounds.Height, Planes = 1, BitCount = 32 };
            _memory = CreateCompatibleDC(IntPtr.Zero);
            _bitmap = CreateDIBSection(_memory, ref header, 0, out var bits, IntPtr.Zero, 0);
            if (_bitmap == IntPtr.Zero) { DeleteDC(_memory); throw new InvalidOperationException("No memory for the transition."); }
            _previous = SelectObject(_memory, _bitmap);
            _painter = new TransitionPainter(bits, bounds.Width, bounds.Height);
            var data = picture.LockBits(new Rectangle(Point.Empty, bounds.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            Buffer.MemoryCopy((void*)data.Scan0, (void*)bits, (long)bounds.Width * bounds.Height * 4, (long)bounds.Width * bounds.Height * 4);
            picture.UnlockBits(data);

            CreateHandle(new System.Windows.Forms.CreateParams
            {
                Caption = WindowTitle, Style = NativeMethods.WS_POPUP,
                ExStyle = NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED,
                X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height
            });
            var off = 1;
            NativeMethods.DwmSetWindowAttribute(Handle, 3, ref off, sizeof(int)); // Windows' own fade.
            SetAlpha(0);
            var desktop = DesktopWidgetWindow.FindDesktop();
            var above = desktop == IntPtr.Zero ? IntPtr.Zero : NativeMethods.GetWindow(desktop, 3);
            // A window covering the whole screen makes Windows treat it as a fullscreen app.
            NativeMethods.SetWindowPos(Handle, above, bounds.X, bounds.Y, bounds.Width, bounds.Height - 1,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            Present(new Rectangle(Point.Empty, bounds.Size));
        }

        public void SetAlpha(byte alpha)
        {
            if (alpha == _alpha) return;
            _alpha = alpha;
            NativeMethods.SetLayeredWindowAttributes(Handle, 0, alpha, NativeMethods.LWA_ALPHA);
        }

        public void Begin(Scene scene) => _painter.Begin(scene.New, scene.Field, scene.Glow, scene.Line, scene.Options);

        public void Detach() => _painter.Detach();

        public void Draw(float progress)
        {
            var dirty = _painter.Draw(progress);
            if (!dirty.IsEmpty) Present(dirty);
        }

        private void Present(Rectangle area)
        {
            GdiFlush();
            var context = GetDC(Handle);
            BitBlt(context, area.X, area.Y, area.Width, area.Height, _memory, area.X, area.Y, 0xCC0020);
            ReleaseDC(Handle, context);
        }

        protected override void WndProc(ref System.Windows.Forms.Message message)
        {
            if (message.Msg == 0x14 /* WM_ERASEBKGND */) { message.Result = 1; return; }
            base.WndProc(ref message);
            if (message.Msg == 0xF /* WM_PAINT */ && !_disposed) Present(new Rectangle(Point.Empty, Bounds.Size));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Detach();
            DestroyHandle();
            _painter.Dispose();
            SelectObject(_memory, _previous);
            DeleteObject(_bitmap);
            DeleteDC(_memory);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant, Colors;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SystemParametersInfo(uint action, uint size, System.Text.StringBuilder value, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr context);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr context);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr context);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr context, IntPtr item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr context, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
}
