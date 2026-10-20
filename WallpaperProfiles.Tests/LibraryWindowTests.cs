using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Reflection;
using System.Net.Http;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Coordination;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.UI;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed partial class LibraryWindowTests
{
    [Fact]
    public void Gallery_FilterOrganizeAndChooseMedia_RoundTripsThroughTheWindow()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "belie-gallery-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            System.Windows.Application? app = null;
            try
            {
                App.ConfigureRendering(liveWallpaper: false, interactiveShell: true);
                Assert.Equal(System.Windows.Interop.RenderMode.Default, RenderOptions.ProcessRenderMode);
                App.ConfigureRendering(liveWallpaper: false);
                Assert.Equal(System.Windows.Interop.RenderMode.SoftwareOnly, RenderOptions.ProcessRenderMode);
                app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Belie;component/UI/Theme.xaml")
                });
                var iconExport = Environment.GetEnvironmentVariable("BELIE_APP_ICON_EXPORT");
                if (!string.IsNullOrEmpty(iconExport))
                    File.WriteAllBytes(iconExport, UiAppearance.IconBytes(UiAppearance.Find("Sage"), 16, 20, 24, 32, 40, 48, 64, 128, 256));
                var imagePath = Path.Combine(dir, "forest.png");
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                {
                    drawing.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 78, 62)), null, new Rect(0, 0, 640, 360));
                }
                var bitmap = new RenderTargetBitmap(640, 360, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var output = File.Create(imagePath)) encoder.Save(output);

                var store = new LibraryStore(Path.Combine(dir, "library.json"));
                var assets = Enumerable.Range(1, 49).Select(i => new WallpaperAsset
                {
                    FilePath = Path.Combine(dir, $"missing-{i:00}.jpg"), Name = $"Wallpaper {i:00}"
                }).ToList();
                assets.Insert(0, new WallpaperAsset
                {
                    FilePath = imagePath, Name = "Forest", IsFavorite = true,
                    Collection = "All collections", Tags = new() { "calm" }
                });
                store.Save(assets);
                using var marketplaceHandler = new MarketplaceHandler(File.ReadAllBytes(imagePath));
                using var marketplaceHttp = new HttpClient(marketplaceHandler);
                var window = new LibraryWindow(store, Array.Empty<string>(), new WallhavenClient(marketplaceHttp, throttle: false),
                    Path.Combine(dir, "downloads")) { ShowInTaskbar = false, Opacity = 0 };
                var timeout = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                timeout.Tick += (_, _) =>
                {
                    failure = new TimeoutException("The library UI check did not complete.");
                    window.Close();
                };
                window.Loaded += async (_, _) =>
                {
                    try
                    {
                        var gallery = (ListBox)window.FindName("Gallery");
                        var search = (TextBox)window.FindName("SearchBox");
                        var collection = (ComboBox)window.FindName("CollectionFilter");
                        var favorites = (CheckBox)window.FindName("FavoritesFilter");
                        var use = (Button)window.FindName("UseButton");
                        Assert.Equal(48, gallery.Items.Count);
                        Click(window, "NextButton");
                        Assert.Equal(2, gallery.Items.Count);
                        gallery.SelectedIndex = 0;
                        Assert.False(use.IsEnabled);

                        search.Text = "calm";
                        Assert.Single(gallery.Items.Cast<object>());
                        search.Text = "";
                        collection.SelectedIndex = 1;
                        Assert.Single(gallery.Items.Cast<object>());
                        collection.SelectedIndex = 0;
                        favorites.IsChecked = true;
                        Assert.Single(gallery.Items.Cast<object>());
                        gallery.SelectedIndex = 0;
                        Assert.True(use.IsEnabled);
                        await WaitUntil(() => TypeDescriptor.GetProperties(gallery.SelectedItem)["Thumbnail"]?.GetValue(gallery.SelectedItem) != null);

                        ((TextBox)window.FindName("AssetNameBox")).Text = "Forest retreat";
                        ((TextBox)window.FindName("TagsBox")).Text = "calm, green, CALM";
                        ((TextBox)window.FindName("AssetCollectionBox")).Text = "Focus";
                        Click(window, "SaveDetailsButton");
                        await WaitUntil(() => ((Button)window.FindName("ImportFilesButton")).IsEnabled);
                        var saved = store.Load().First(a => a.FilePath == imagePath);
                        Assert.Equal("Forest retreat", saved.Name);
                        Assert.Equal("Focus", saved.Collection);
                        Assert.Equal(new[] { "calm", "green" }, saved.Tags);
                        Assert.True(saved.IsFavorite);
                        Assert.Equal("Focus", ((TextBox)window.FindName("AssetCollectionBox")).Text);

                        var previewPath = Environment.GetEnvironmentVariable("BELIE_LIBRARY_PREVIEW");
                        if (!string.IsNullOrEmpty(previewPath))
                        {
                            window.UpdateLayout();
                            var preview = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                            var background = new DrawingVisual();
                            using (var drawing = background.RenderOpen())
                            {
                                drawing.DrawRectangle(window.Background, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
                            }
                            preview.Render(background);
                            // Render the content so a hidden test window still produces a visible preview.
                            preview.Render((Visual)window.Content);
                            var png = new PngBitmapEncoder();
                            png.Frames.Add(BitmapFrame.Create(preview));
                            using var output = File.Create(previewPath);
                            png.Save(output);
                        }
                        await CheckMarketplaceAsync(window, store, marketplaceHandler);
                        gallery.SelectedIndex = 0;
                        Click(window, "UseButton");
                    }
                    catch (Exception ex) { failure = ex; window.Close(); }
                };
                timeout.Start();
                Assert.True(window.ShowDialog());
                timeout.Stop();
                Assert.Equal(imagePath, window.Result?.FilePath);
                if (failure == null)
                {
                    CheckWorkspaceNavigation(dir, store, imagePath);
                    CheckWallpaperStudio(dir, imagePath);
                    CheckAmbientPlayback(dir);
                }
            }
            catch (Exception ex) { failure ??= ex; }
            finally
            {
                if (app != null)
                {
                    foreach (var editor in app.Windows.OfType<ProfileEditorWindow>()) editor.ChooseDraftAction = () => DraftChoice.Discard;
                    foreach (var main in app.Windows.OfType<MainWindow>()) main.ChooseDraftAction = () => DraftChoice.Discard;
                }
                app?.Shutdown();
                Directory.Delete(dir, recursive: true);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(90)), "The library UI thread did not exit.");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Click(Window window, string name)
        => ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void CheckPatchNotes(MainWindow main)
    {
        Exception? failure = null;
        main.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var notes = System.Windows.Application.Current.Windows.OfType<ReleaseNotesWindow>().Single();
            try
            {
                Assert.Equal("Belie settings", notes.Owner.Title);
                var paragraphs = VisualChildren(notes).OfType<TextBlock>()
                    .Select(text => new System.Windows.Documents.TextRange(text.ContentStart, text.ContentEnd).Text).ToArray();
                Assert.Contains(paragraphs, text => text.Contains("What changed"));
                Assert.Contains(paragraphs, text => text.Contains("Customize wallpaper"));
                Assert.DoesNotContain(paragraphs, text => text.Contains("**"));
                RenderWorkspace(notes, "patch-notes", 700, 740);
                notes.Width = 460; notes.Height = 560; notes.UpdateLayout();
                RenderWorkspace(notes, "patch-notes-compact", 460, 560);
                var scroll = VisualChildren(notes).OfType<ScrollViewer>().First(viewer => viewer.Content is StackPanel);
                Assert.True(scroll.ScrollableHeight > 0);
                scroll.ScrollToEnd(); notes.UpdateLayout();
                RenderWorkspace(notes, "patch-notes-guide", 460, 560);
                var close = VisualChildren(notes).OfType<Button>().Single(button => Equals(button.Content, "Close"));
                var position = close.TranslatePoint(new System.Windows.Point(), (UIElement)notes.Content);
                Assert.InRange(position.Y + close.ActualHeight, 1, ((FrameworkElement)notes.Content).ActualHeight);
            }
            catch (Exception ex) { failure = ex; }
            finally { notes.Close(); }
        }));
        Click(main, "WhatsNewButton");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void CheckPreviewScaling(DesktopCanvasStore store, string imagePath)
    {
        var type = typeof(DesktopWidgetWindow);
        var fields = new[] { "_wallpaper", "_wallpaperKey", "_wallpaperStyle", "_wallpaperChecked" }
            .Select(name => type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!).ToArray();
        var originals = fields.Select(field => field.GetValue(null)).ToArray();
        var savedCanvas = File.ReadAllText(store.FilePath);
        try
        {
            fields[0].SetValue(null, ThumbnailLoader.Load(imagePath, 1920));
            fields[1].SetValue(null, "preview-test"); fields[2].SetValue(null, "2"); fields[3].SetValue(null, DateTime.UtcNow);
            var screen = System.Windows.Forms.Screen.PrimaryScreen!;
            var widget = new DesktopWidget { Title = "Preview", Width = 320, Height = 200, GlassEffect = true,
                X = screen.WorkingArea.Right - 10, Y = screen.WorkingArea.Bottom - 10 };
            var renderer = new DesktopWidgetWindow(widget, store);
            try
            {
                foreach (var factor in new[] { 1.25, 1.5, 2 })
                {
                    var bounds = renderer.UpdatePreview(widget, new Matrix(factor, 0, 0, factor, 0, 0));
                    Assert.Equal(320 * factor, bounds.Width); Assert.Equal(200 * factor, bounds.Height);
                    Assert.Equal(screen.WorkingArea.Right - bounds.Width, bounds.X);
                    Assert.Equal(screen.WorkingArea.Bottom - bounds.Height, bounds.Y);
                    var frost = (Border)type.GetField("_frost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
                    var brush = Assert.IsType<ImageBrush>(frost.Background);
                    Assert.Equal((320 + 96) * factor / screen.Bounds.Width, brush.Viewbox.Width, precision: 6);
                    Assert.Equal((200 + 96) * factor / screen.Bounds.Height, brush.Viewbox.Height, precision: 6);
                    Assert.Equal((bounds.X - 48 * factor - screen.Bounds.Left) / screen.Bounds.Width, brush.Viewbox.X, precision: 6);
                    Assert.Equal((bounds.Y - 48 * factor - screen.Bounds.Top) / screen.Bounds.Height, brush.Viewbox.Y, precision: 6);
                }
                Assert.Equal(screen.WorkingArea.Right - 10, widget.X);
                Assert.Equal(screen.WorkingArea.Bottom - 10, widget.Y);
                Assert.Equal(savedCanvas, File.ReadAllText(store.FilePath));
            }
            finally { renderer.Close(); }
        }
        finally { for (var i = 0; i < fields.Length; i++) fields[i].SetValue(null, originals[i]); }
    }

    private static void RenderWorkspace(Window window, string view, int width, int height, double dpi = 96)
    {
        if (window is MainWindow) { window.Width = width; window.Height = height; window.UpdateLayout(); }
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width - 2, height - 2));
        content.Arrange(new Rect(0, 0, width - 2, height - 2));
        content.UpdateLayout();
        var path = Environment.GetEnvironmentVariable("BELIE_WORKSPACE_PREVIEW");
        if (string.IsNullOrEmpty(path)) return;
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(210) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)(width * dpi / 96), (int)(height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(path, view + ".png"));
        png.Save(output);
    }

    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in VisualChildren(child)) yield return descendant;
        }
    }

    private static void CheckAmbientPlayback(string dir)
    {
        // Hosted CI runners have no audio output; keep the rest of the gallery test active.
        using (var devices = new NAudio.CoreAudioApi.MMDeviceEnumerator())
        {
            if (!devices.HasDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render,
                    NAudio.CoreAudioApi.Role.Multimedia)) return;
        }
        var path = Path.Combine(dir, "silent-ambient.wav");
        var samples = new byte[3200]; // 200 ms of silent 8 kHz, mono, 16-bit PCM.
        using (var output = new BinaryWriter(File.Create(path)))
        {
            output.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            output.Write(36 + samples.Length);
            output.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            output.Write(16);
            output.Write((ushort)1);
            output.Write((ushort)1);
            output.Write(8000);
            output.Write(16000);
            output.Write((ushort)2);
            output.Write((ushort)16);
            output.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            output.Write(samples.Length);
            output.Write(samples);
        }
        using var scene = new WallpaperProfiles.Engine.SceneController(new ResourceDictionary());
        scene.Apply(new WallpaperProfile { AmbientAudioPath = path, AmbientVolume = 25, AmbientMuted = true });
        var player = (MediaPlayer)typeof(WallpaperProfiles.Engine.SceneController)
            .GetField("_player", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
        Assert.NotNull(player);
        Assert.Equal(0.25, player.Volume);
        Assert.True(player.IsMuted);
        var loops = 0;
        player.MediaEnded += (_, _) => loops++;
        var frame = new System.Windows.Threading.DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(8);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += (_, _) =>
        {
            if (loops >= 2 || !scene.HasAudio || DateTime.UtcNow >= deadline) frame.Continue = false;
        };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        timer.Stop();
        Assert.True(loops >= 2, "The silent ambient track did not loop: " + scene.AudioStatus);
        scene.ToggleMuted();
        Assert.False(scene.IsMuted);
        Assert.False(player.IsMuted);
        Assert.StartsWith("Playing", scene.AudioStatus);
        scene.Apply(new WallpaperProfile());
        Assert.False(scene.HasAudio);
        Assert.Equal("No ambient track", scene.AudioStatus);
    }

    private static void MoveWallpaper(Window window, string path, string step)
    {
        window.UpdateLayout();
        var list = (WallpaperThumbnailList)window.FindName("ProfileWallpaperList");
        PumpUntil(() => VisualChildren(list).OfType<SourceThumbnail>().All(image => image.Source != null));
        window.UpdateLayout();
        var originalThumbnails = VisualChildren(list).OfType<SourceThumbnail>().ToDictionary(image => image.FilePath);
        var originalImages = originalThumbnails.ToDictionary(pair => pair.Key, pair => pair.Value.Source);
        var preview = (MonitorThumbnail)window.FindName(window is MainWindow ? "PreviewMon" : "EditorPreview");
        var previewImage = preview.ScreenSource;
        var previewPath = preview.PreviewFilePath;
        var index = list.Items.IndexOf(path);
        var direction = int.Parse(step, CultureInfo.InvariantCulture);
        var target = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(index + direction);
        var point = target.TranslatePoint(new System.Windows.Point(direction < 0 ? 1 : target.ActualWidth - 1, target.ActualHeight / 2), list);
        Assert.Empty(VisualChildren(list).OfType<Button>());
        var data = list.CreateDragData(path);
        System.Windows.DragEventArgs DragEvent(System.Windows.DragDropKeyStates keys, RoutedEvent routedEvent)
        {
            var args = (System.Windows.DragEventArgs)Activator.CreateInstance(typeof(System.Windows.DragEventArgs),
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { data, keys, System.Windows.DragDropEffects.Move, list, point }, CultureInfo.InvariantCulture)!;
            args.RoutedEvent = routedEvent;
            return args;
        }
        var over = DragEvent(System.Windows.DragDropKeyStates.LeftMouseButton, System.Windows.DragDrop.DragOverEvent);
        list.RaiseEvent(over);
        Assert.Equal(System.Windows.DragDropEffects.Move, over.Effects);
        var drop = DragEvent(System.Windows.DragDropKeyStates.None, System.Windows.DragDrop.DropEvent);
        list.RaiseEvent(drop);
        Assert.Equal(System.Windows.DragDropEffects.Move, drop.Effects);
        Assert.Equal(originalThumbnails.Count, list.Items.Count);
        window.UpdateLayout();
        var updatedThumbnails = VisualChildren(list).OfType<SourceThumbnail>().ToArray();
        Assert.Equal(originalThumbnails.Count, updatedThumbnails.Length);
        foreach (var thumbnail in updatedThumbnails)
        {
            Assert.Same(originalThumbnails[thumbnail.FilePath], thumbnail);
            Assert.Same(originalImages[thumbnail.FilePath], thumbnail.Source);
        }
        Assert.Same(previewImage, preview.ScreenSource);
        Assert.Equal(previewPath, preview.PreviewFilePath);
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { if (condition() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start();
        try { System.Windows.Threading.Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.True(condition(), "Profile wallpaper thumbnails did not finish updating.");
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100; i++)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
        Assert.Fail("The library operation did not complete.");
    }
}
