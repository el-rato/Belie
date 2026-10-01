using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Reflection;
using WallpaperProfiles.Coordination;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.UI;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed class LibraryWindowTests
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
                app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Belie;component/UI/Theme.xaml")
                });
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
                var window = new LibraryWindow(store, Array.Empty<string>()) { ShowInTaskbar = false, Opacity = 0 };
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
                        Click(window, "UseButton");
                    }
                    catch (Exception ex) { failure = ex; window.Close(); }
                };
                timeout.Start();
                Assert.True(window.ShowDialog());
                timeout.Stop();
                Assert.Equal(imagePath, window.Result?.FilePath);
                if (failure == null) CheckWorkspaceNavigation(dir, store, imagePath);
            }
            catch (Exception ex) { failure ??= ex; }
            finally
            {
                app?.Shutdown();
                Directory.Delete(dir, recursive: true);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "The library UI thread did not exit.");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Click(Window window, string name)
        => ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void CheckWorkspaceNavigation(string dir, LibraryStore libraryStore, string imagePath)
    {
        var profiles = new ProfileStore(Path.Combine(dir, "profiles"));
        var settings = new SettingsStore(Path.Combine(dir, "settings.json"));
        var profile = new WallpaperProfile { Name = "Focus", SlideshowIntervalMinutes = 5 };
        profiles.Save(profile);
        using var coordinator = new WallpaperCoordinator(profiles, settings, new AppSettings());
        typeof(WallpaperCoordinator).GetField("_profiles", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(coordinator, new List<WallpaperProfile> { profile });
        var main = new MainWindow(coordinator, profiles, settings, libraryStore);
        typeof(MainWindow).GetMethod("BuildRail", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
        typeof(MainWindow).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { profile.Id });
        var profilePane = (FrameworkElement)main.FindName("ProfilePane");
        var libraryPane = (ContentControl)main.FindName("LibraryPane");

        Click(main, "LibraryNavButton");
        Assert.Equal(Visibility.Collapsed, profilePane.Visibility);
        Assert.Equal(Visibility.Visible, libraryPane.Visibility);
        var library = (LibraryWindow)typeof(MainWindow).GetField("_libraryView", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        ((FrameworkElement)library.FindName("LibraryRoot")).RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var gallery = (ListBox)library.FindName("Gallery");
        Assert.Equal(48, gallery.Items.Count);
        Assert.Equal(main.FindResource("TextPrimaryBrush"), gallery.Foreground);
        Assert.Same(libraryPane.Content, library.FindName("LibraryRoot"));
        Assert.Equal(Visibility.Visible, ((Button)library.FindName("BackButton")).Visibility);

        RenderWorkspace(main, "library", 1040, 790);
        RenderWorkspace(main, "library-compact", 880, 650);
        Click(library, "BackButton");
        Assert.Equal(Visibility.Visible, profilePane.Visibility);
        Assert.Equal(Visibility.Collapsed, libraryPane.Visibility);
        Click(main, "LibraryNavButton");
        Assert.Same(library, typeof(MainWindow).GetField("_libraryView", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main));
        Click(main, "ProfilesNavButton");
        Assert.Equal(Visibility.Visible, profilePane.Visibility);
        Click(main, "LibraryNavButton");
        gallery.SelectedIndex = 0;
        Click(library, "UseButton");
        Assert.Equal(Visibility.Visible, profilePane.Visibility);
        Assert.Equal(Visibility.Collapsed, libraryPane.Visibility);
        Assert.Equal(imagePath, ((TextBox)main.FindName("FolderBox")).Text);
        Assert.Equal("Focus", ((TextBox)main.FindName("NameBox")).Text);
        Assert.Equal("", Assert.Single(profiles.LoadAll()).FolderPath);
        RenderWorkspace(main, "profiles", 1040, 790);
        RenderWorkspace(main, "profiles-compact", 880, 650);
        library.Close();
    }

    private static void RenderWorkspace(MainWindow window, string view, int width, int height)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width - 2, height - 2));
        content.Arrange(new Rect(0, 0, width - 2, height - 2));
        content.UpdateLayout();
        var path = Environment.GetEnvironmentVariable("BELIE_WORKSPACE_PREVIEW");
        if (string.IsNullOrEmpty(path)) return;
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(path, view + ".png"));
        png.Save(output);
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
