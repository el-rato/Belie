using System.ComponentModel;
using System.Globalization;
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
                App.ConfigureRendering(liveWallpaper: false);
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
                if (failure == null)
                {
                    CheckWorkspaceNavigation(dir, store, imagePath);
                    CheckAmbientPlayback(dir);
                }
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
        var clock = new TestClock();
        using var coordinator = new WallpaperCoordinator(profiles, settings, new AppSettings(), clock, _ => { });
        typeof(WallpaperCoordinator).GetField("_profiles", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(coordinator, new List<WallpaperProfile> { profile });
        var main = new MainWindow(coordinator, profiles, settings, libraryStore);
        // Present real popup controls without invoking startup and registry changes.
        main.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), main,
            typeof(MainWindow).GetMethod("OnLoaded", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!);
        main.Opacity = 0; main.ShowInTaskbar = false; main.ShowActivated = false; main.Show();
        typeof(MainWindow).GetMethod("BuildRail", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
        typeof(MainWindow).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { profile.Id });
        var profilePane = (FrameworkElement)main.FindName("ProfilePane");
        var libraryPane = (ContentControl)main.FindName("LibraryPane");
        profile.Schedule.Add(new ScheduleRule { DaysOfWeek = new() { DayOfWeek.Monday, DayOfWeek.Friday },
            StartTime = new TimeOnly(22, 0), EndTime = new TimeOnly(6, 0) });
        profile.EventTriggers.Add(new WallpaperProfiles.Models.EventTrigger { Type = TriggerType.BelowBatteryPercent, Condition = "15", Priority = 8 });
        typeof(MainWindow).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { profile.Id });
        Assert.Contains("22:00 – 06:00 · overnight", ((TextBlock)main.FindName("ScheduleSummary")).Text);
        Assert.Contains(CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(DayOfWeek.Monday), ((TextBlock)main.FindName("ScheduleSummary")).Text);
        Assert.Equal("battery ≤ 15% · priority 8", ((TextBlock)main.FindName("TriggerSummary")).Text);
        Assert.Equal("Edit rules", ((Button)main.FindName("EditRulesButton")).Content);
        ((TabControl)main.FindName("ProfileTabs")).SelectedIndex = 2;
        RenderWorkspace(main, "profile-rules", 880, 650);
        foreach (var (button, tab) in new[] { ("EditProfileButton", 0), ("EditRulesButton", 2) })
        {
            Exception? editorFailure = null;
            main.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                var editor = System.Windows.Application.Current.Windows.OfType<ProfileEditorWindow>().Single(window => window.Owner == main);
                try
                {
                    Assert.Equal(tab, ((TabControl)editor.FindName("EditorTabs")).SelectedIndex);
                    Assert.Single(((ItemsControl)editor.FindName("RulesList")).Items);
                }
                catch (Exception ex) { editorFailure = ex; }
                finally { editor.Close(); }
            }));
            Click(main, button);
            if (editorFailure != null) throw editorFailure;
        }
        profile.Schedule.Clear(); profile.EventTriggers.Clear();
        typeof(MainWindow).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { profile.Id });
        ((TabControl)main.FindName("ProfileTabs")).SelectedIndex = 0;

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
        var profileTabs = (TabControl)main.FindName("ProfileTabs");
        Assert.Equal(4, profileTabs.Items.Count);
        profileTabs.SelectedIndex = 1;
        RenderWorkspace(main, "profiles-playback", 1040, 790);
        Assert.Equal("Focus", ((TextBox)main.FindName("NameBox")).Text);
        Assert.Equal(imagePath, ((TextBox)main.FindName("FolderBox")).Text);
        profileTabs.SelectedIndex = 0;
        Click(main, "SettingsButton");
        Assert.True(((System.Windows.Controls.Primitives.Popup)main.FindName("SettingsPopup")).IsOpen);
        Click(main, "SettingsButton");
        Assert.False(((System.Windows.Controls.Primitives.Popup)main.FindName("SettingsPopup")).IsOpen);
        CheckAppearance(main, coordinator, settings, profile);
        RenderWorkspace(main, "profiles", 1040, 790);
        RenderWorkspace(main, "profiles-compact", 880, 650);
        CheckActivityAndSceneControls(main, coordinator, clock, profile, imagePath);
        CheckDesktopCanvasPanel(main, dir, imagePath);
        library.Close();
    }

    private static void CheckAppearance(MainWindow main, WallpaperCoordinator coordinator, SettingsStore settings, WallpaperProfile profile)
    {
        var picker = (ListBox)main.FindName("ThemePicker");
        Assert.Equal(5, picker.Items.Count);
        Assert.Equal("Sage", UiAppearance.Find("unknown-theme").Name);
        var icons = new HashSet<string>();
        foreach (var theme in UiAppearance.Themes)
        {
            picker.SelectedItem = theme;
            main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            Assert.Equal(theme.Name, UiAppearance.Current.Name);
            Assert.Equal(theme.Name, settings.Load().UiTheme);
            Assert.Equal((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.Background), ((SolidColorBrush)main.Background).Color);
            Assert.Equal((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.Text), ((SolidColorBrush)main.Foreground).Color);
            var icon = (BitmapSource)main.Icon;
            var pixels = new byte[icon.PixelWidth * icon.PixelHeight * 4]; icon.CopyPixels(pixels, icon.PixelWidth * 4, 0);
            icons.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels)));
            using var trayIcon = UiAppearance.CreateTrayIcon();
            Assert.Equal(32, trayIcon.Width);
            RenderWorkspace(main, "theme-" + theme.Name.ToLowerInvariant(), 1040, 790);
        }
        Assert.Equal(5, icons.Count);
        picker.SelectedItem = UiAppearance.Find("Ember");
        coordinator.SwitchManually(profile.Id);
        Assert.Equal("Ember", settings.Load().UiTheme);
        Assert.Equal(profile.Id.ToString(), settings.Load().LastActiveProfileId);
        var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(main);
        Assert.NotNull(chrome);
        Assert.Equal(new Thickness(0), chrome.GlassFrameThickness);
        if (Environment.OSVersion.Version.Build >= 22000) Assert.True(UiAppearance.UpdateFrame(main));
        Click(main, "MaximizeButton");
        main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert.Equal(WindowState.Maximized, main.WindowState);
        Assert.Equal("Restore", ((Button)main.FindName("MaximizeButton")).ToolTip);
        var handle = new System.Windows.Interop.WindowInteropHelper(main).Handle;
        var work = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        Assert.True(WallpaperProfiles.Infrastructure.NativeMethods.GetWindowRect(handle, out var bounds));
        Assert.Equal(work.Left, bounds.Left);
        Assert.Equal(work.Top, bounds.Top);
        Assert.Equal(work.Right, bounds.Right);
        Assert.Equal(work.Bottom, bounds.Bottom);
        Click(main, "MaximizeButton");
        main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert.Equal(WindowState.Normal, main.WindowState);
        Assert.Equal("Maximize", ((Button)main.FindName("MaximizeButton")).ToolTip);
        profile.SceneAccent = "#B7CDBC";
        coordinator.SwitchManually(profile.Id);
        picker.SelectedItem = UiAppearance.Find("Midnight");
        Assert.Equal((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B7CDBC"), System.Windows.Application.Current.Resources["SceneAccentColor"]);
        profile.SceneAccent = "";
        coordinator.SwitchManually(profile.Id);
        Assert.Equal((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(UiAppearance.Current.Accent), System.Windows.Application.Current.Resources["SceneAccentColor"]);
        picker.SelectedItem = UiAppearance.Find("Sage");
    }

    private static void CheckDesktopCanvasPanel(MainWindow main, string dir, string imagePath)
    {
        var store = new DesktopCanvasStore(Path.Combine(dir, "canvas.json"));
        var starts = 0;
        var widgetProfiles = new List<WallpaperProfile> { new() { Name = "Study" }, new() { Name = "Chill" } };
        var view = new DesktopCanvasView(store, () => starts++, profiles: () => widgetProfiles);
        var pane = (ContentControl)main.FindName("CanvasPane");
        pane.Content = view;
        Click(main, "CanvasNavButton");
        main.UpdateLayout();
        main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert.Equal(Visibility.Visible, pane.Visibility);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)main.FindName("DashboardPane")).Visibility);
        void Press(string name) => ((Button)view.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(((ComboBox)view.FindName("WidgetFont")).Items.Count > 7);
        Assert.Contains("Segoe UI", ((ComboBox)view.FindName("WidgetFont")).Items.Cast<string>());
        Press("AddWidgetButton");
        Assert.True(((System.Windows.Controls.Primitives.Popup)view.FindName("WidgetTypePicker")).IsOpen);
        Assert.False(File.Exists(store.FilePath));
        Press("AddNoteButton");
        Assert.False(((System.Windows.Controls.Primitives.Popup)view.FindName("WidgetTypePicker")).IsOpen);
        ((TextBox)view.FindName("WidgetTitle")).Text = "My project";
        ((TextBox)view.FindName("WidgetContent")).Text = "Sketch ideas\nBuild something useful";
        Press("SaveWidgetButton");
        Assert.True(store.Load().Enabled);
        Assert.Equal("My project", Assert.Single(store.Load().Widgets).Title);
        Assert.Equal(1, starts);
        var savedBeforeEditing = File.ReadAllText(store.FilePath);
        Press("WidgetStyleTab");
        Assert.Equal(Visibility.Visible, ((FrameworkElement)VisualTreeHelper.GetParent((ComboBox)view.FindName("WidgetFont"))).Visibility);
        Press("WidgetLayoutTab");
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)VisualTreeHelper.GetParent((ComboBox)view.FindName("WidgetFont"))).Visibility);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)VisualTreeHelper.GetParent((Slider)view.FindName("WidgetWidth"))).Visibility);
        Assert.Equal(savedBeforeEditing, File.ReadAllText(store.FilePath));
        Press("WidgetContentTab");
        var previewBox = (Viewbox)view.FindName("WidgetPreview");
        var previewCard = (Border)previewBox.Child;
        var previewSurface = (Grid)previewCard.Child;
        var previewGrid = (Grid)previewSurface.Children[1];
        var previewBody = previewGrid.Children.OfType<ContentControl>().Single();
        var previewHeader = previewGrid.Children.OfType<Border>().Single();
        ((TextBox)view.FindName("WidgetContent")).Text = "This is an unsaved draft.";
        ((ComboBox)view.FindName("WidgetFont")).SelectedItem = "Georgia";
        main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert.Equal("This is an unsaved draft.", ((TextBlock)((ScrollViewer)previewBody.Content).Content).Text);
        Assert.Equal("Georgia", ((TextBlock)((ScrollViewer)previewBody.Content).Content).FontFamily.Source);
        ((ComboBox)view.FindName("WidgetFont")).Text = "Consolas";
        main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert.Equal("Consolas", ((TextBlock)((ScrollViewer)previewBody.Content).Content).FontFamily.Source);
        ((Slider)view.FindName("WidgetFontSize")).Value = 32;
        ((Slider)view.FindName("WidgetWidth")).Value = 420;
        ((TextBox)view.FindName("WidgetTitle")).Text = "";
        ((CheckBox)view.FindName("WidgetItalic")).IsChecked = true;
        ((CheckBox)view.FindName("WidgetLocked")).IsChecked = true;
        Assert.Equal(32, ((TextBlock)((ScrollViewer)previewBody.Content).Content).FontSize);
        Assert.Equal(FontStyles.Italic, ((TextBlock)((ScrollViewer)previewBody.Content).Content).FontStyle);
        Assert.Equal(420, previewCard.Width);
        Assert.Equal(Visibility.Collapsed, previewHeader.Visibility);
        Assert.Equal(Visibility.Collapsed, previewGrid.Children.OfType<System.Windows.Controls.Primitives.Thumb>().Single().Visibility);
        Press("StyleCrimsonButton");
        Assert.Equal("#FF270D15", ((SolidColorBrush)previewCard.Background).Color.ToString());
        ((CheckBox)view.FindName("WidgetGlassEffect")).IsChecked = true;
        Assert.Equal(Visibility.Visible, previewSurface.Children[0].Visibility);
        Assert.Equal(savedBeforeEditing, File.ReadAllText(store.FilePath));
        Assert.Equal(1, starts);
        ((ListBox)view.FindName("WidgetList")).SelectedIndex = -1;
        ((ListBox)view.FindName("WidgetList")).SelectedIndex = 0;
        Press("StylePaperButton");
        ((Slider)view.FindName("WidgetFontSize")).Value = 28;
        ((ComboBox)view.FindName("WidgetAlignment")).SelectedItem = WidgetTextAlignment.Center;
        ((CheckBox)view.FindName("WidgetBold")).IsChecked = true;
        ((CheckBox)view.FindName("WidgetHeader")).IsChecked = false;
        ((Slider)view.FindName("WidgetOpacity")).Value = 80;
        Press("SaveWidgetButton");
        var styled = Assert.Single(store.Load().Widgets);
        Assert.Equal("Georgia", styled.FontFamily);
        Assert.Equal(28, styled.FontSize);
        Assert.Equal("#EEE8D8", styled.BackgroundColor);
        Assert.Equal(WidgetTextAlignment.Center, styled.Alignment);
        Assert.True(styled.Bold);
        Assert.False(styled.ShowHeader);
        Assert.Equal(.8, styled.Opacity);
        Assert.NotNull(((Viewbox)view.FindName("WidgetPreview")).Child);
        var widgetProfile = (ComboBox)view.FindName("WidgetProfile");
        Assert.Equal(3, widgetProfile.Items.Count);
        widgetProfile.SelectedIndex = 1;
        Press("SaveWidgetButton");
        Assert.Equal(widgetProfiles[0].Id, Assert.Single(store.Load().Widgets).ProfileId);
        var scopeFilter = (ComboBox)view.FindName("WidgetScopeFilter");
        scopeFilter.SelectedIndex = 1; // General only
        Assert.Empty(((ListBox)view.FindName("WidgetList")).Items);
        scopeFilter.SelectedIndex = 2; // Study
        Assert.Single(((ListBox)view.FindName("WidgetList")).Items);
        scopeFilter.SelectedIndex = 0;
        widgetProfiles.RemoveAt(0);
        view.RefreshProfiles();
        Assert.Equal("Missing profile", TypeDescriptor.GetProperties(widgetProfile.SelectedItem)["Label"]!.GetValue(widgetProfile.SelectedItem));
        widgetProfile.SelectedIndex = 0;
        Press("SaveWidgetButton");
        Assert.Null(Assert.Single(store.Load().Widgets).ProfileId);
        ((TextBox)view.FindName("WidgetTextColor")).Text = "red";
        Press("SaveWidgetButton");
        Assert.Equal("#282A25", Assert.Single(store.Load().Widgets).TextColor);
        Assert.Contains("#RRGGBB", ((TextBlock)view.FindName("CanvasMessage")).Text);
        ((TextBox)view.FindName("WidgetTextColor")).Text = "#282A25";
        Press("SaveWidgetButton");
        Press("DuplicateWidgetButton");
        Assert.Equal(2, store.Load().Widgets.Count);
        Assert.Equal("My project copy", store.Load().Widgets.Last().Title);
        Assert.Equal(28, store.Load().Widgets.Last().FontSize);
        Press("DeleteWidgetButton");
        Assert.Single(store.Load().Widgets);
        ((ListBox)view.FindName("WidgetList")).SelectedIndex = 0;
        RenderWorkspace(main, "canvas", 1040, 790);
        RenderWorkspace(main, "canvas-compact", 880, 650);
        var save = (Button)view.FindName("SaveWidgetButton");
        var saveLocation = save.TranslatePoint(new System.Windows.Point(0, 0), (UIElement)main.Content);
        Assert.True(save.ActualHeight > 0);
        Assert.InRange(saveLocation.Y + save.ActualHeight, 1, ((FrameworkElement)main.Content).ActualHeight);
        var editorScroll = (ScrollViewer)view.FindName("WidgetEditorScroll");
        editorScroll.ScrollToEnd();
        main.UpdateLayout();
        var previewLocation = previewBox.TranslatePoint(new System.Windows.Point(0, 0), (UIElement)main.Content);
        Assert.True(previewBox.ActualHeight > 0);
        Assert.InRange(previewLocation.Y, 0, ((FrameworkElement)main.Content).ActualHeight);
        Assert.InRange(previewLocation.Y + previewBox.ActualHeight, 1, ((FrameworkElement)main.Content).ActualHeight);
        var pointer = new System.Drawing.Point(500, 500);
        var widget = new WallpaperProfiles.Engine.DesktopWidgetWindow(Assert.Single(store.Load().Widgets), store, () => pointer);
        RenderWorkspace(widget, "canvas-note", 300, 230);
        widget.Opacity = 0;
        widget.Show();
        var initialWidth = widget.Width;
        var initialHeight = widget.Height;
        var grip = (System.Windows.Controls.Primitives.Thumb)typeof(WallpaperProfiles.Engine.DesktopWidgetWindow)
            .GetField("_resize", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(widget)!;
        grip.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0)
            { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent });
        var scale = PresentationSource.FromVisual(widget)!.CompositionTarget!.TransformToDevice;
        var body = (ContentControl)typeof(WallpaperProfiles.Engine.DesktopWidgetWindow)
            .GetField("_body", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(widget)!;
        var noteText = (TextBlock)((ScrollViewer)body.Content).Content;
        void MovePointer(int x, int y, int previousX = 0, int previousY = 0)
        {
            // Several mouse moves can arrive before the grip has moved to its next rendered position.
            for (var i = 1; i <= 40; i++)
            {
                var dx = previousX + (x - previousX) * i / 40;
                var dy = previousY + (y - previousY) * i / 40;
                pointer = new System.Drawing.Point(500 + (int)Math.Round(dx * scale.M11), 500 + (int)Math.Round(dy * scale.M22));
                grip.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(dx - previousX, dy - previousY)
                    { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
            }
        }
        void CheckRenderedSize(double width, double height)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timeout = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timeout.Tick += (_, _) => frame.Continue = false;
            EventHandler rendered = (_, _) => frame.Continue = false;
            CompositionTarget.Rendering += rendered;
            timeout.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            timeout.Stop();
            CompositionTarget.Rendering -= rendered;
            Assert.InRange(widget.Width, width - 1, width + 1);
            Assert.InRange(widget.Height, height - 1, height + 1);
            var viewport = (ScrollViewer)body.Content;
            Assert.InRange(viewport.ViewportWidth, widget.ActualWidth - 50, widget.ActualWidth - 30);
            Assert.InRange(noteText.ActualWidth, viewport.ViewportWidth - 1, viewport.ViewportWidth + 1);
        }
        MovePointer(-40, 40);
        CheckRenderedSize(initialWidth - 40, initialHeight + 40);
        MovePointer(-80, 80, -40, 40);
        CheckRenderedSize(initialWidth - 80, initialHeight + 80);
        grip.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(-80, 80, false)
            { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
        widget.UpdateLayout();
        Assert.InRange(widget.Width, initialWidth - 81, initialWidth - 79);
        Assert.InRange(noteText.ActualWidth, 1, widget.ActualWidth - 30);
        Assert.InRange(Assert.Single(store.Load().Widgets).Width, initialWidth - 81, initialWidth - 79);
        var secondWidth = widget.Width;
        var secondHeight = widget.Height;
        pointer = new System.Drawing.Point(500, 500);
        grip.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0)
            { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent });
        MovePointer(-300, -300);
        CheckRenderedSize(120, 80);
        MovePointer(20, 20, -300, -300);
        CheckRenderedSize(secondWidth + 20, secondHeight + 20);
        grip.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(20, 20, false)
            { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
        Assert.InRange(Assert.Single(store.Load().Widgets).Width, secondWidth + 19, secondWidth + 21);
        widget.Close();
        Press("StyleGlassButton");
        ((Slider)view.FindName("WidgetFontSize")).Value = 22;
        ((Slider)view.FindName("WidgetWidth")).Value = 350;
        Press("SaveWidgetButton");
        Assert.True(Assert.Single(store.Load().Widgets).GlassEffect);
        CheckPreviewScaling(store, imagePath);
        var glassWidget = new WallpaperProfiles.Engine.DesktopWidgetWindow(Assert.Single(store.Load().Widgets), store);
        RenderWorkspace(glassWidget, "canvas-glass", 350, 230);
        glassWidget.Close();
        using (var host = new WallpaperProfiles.Engine.DesktopCanvasHost(store.FilePath,
            saved => new WallpaperProfiles.Engine.DesktopWidgetWindow(saved, store) { Opacity = 0 }))
        {
            var windows = (Dictionary<Guid, WallpaperProfiles.Engine.DesktopWidgetWindow>)typeof(WallpaperProfiles.Engine.DesktopCanvasHost)
                .GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
            var poll = (System.Windows.Threading.DispatcherTimer)typeof(WallpaperProfiles.Engine.DesktopCanvasHost)
                .GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
            poll.Interval = TimeSpan.FromMinutes(1); // Verify save notifications without the periodic fallback.
            void AwaitUpdate(Func<bool> updated)
            {
                var frame = new System.Windows.Threading.DispatcherFrame();
                var deadline = DateTime.UtcNow.AddSeconds(2);
                var check = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
                check.Tick += (_, _) => { if (updated() || DateTime.UtcNow >= deadline) frame.Continue = false; };
                check.Start();
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                check.Stop();
                Assert.True(updated(), "Saved edits did not reach the running widget.");
            }
            AwaitUpdate(() => windows.Count == 1);
            var generalId = Assert.Single(store.Load().Widgets).Id;
            var studyId = Guid.NewGuid(); var chillId = Guid.NewGuid();
            var studyWidget = new DesktopWidget { ProfileId = studyId, Content = "Study only" };
            var chillWidget = new DesktopWidget { ProfileId = chillId, Content = "Chill only" };
            store.Update(canvas => { canvas.ActiveProfileId = studyId; canvas.Widgets.Add(studyWidget); canvas.Widgets.Add(chillWidget); });
            AwaitUpdate(() => windows.Count == 2 && windows.ContainsKey(generalId) && windows.ContainsKey(studyWidget.Id));
            store.Update(canvas => canvas.ActiveProfileId = chillId);
            AwaitUpdate(() => windows.Count == 2 && windows.ContainsKey(generalId) && windows.ContainsKey(chillWidget.Id));
            store.Update(canvas => { canvas.ActiveProfileId = null; canvas.Widgets.First(widget => widget.Id == generalId).ProfileId = studyId; });
            AwaitUpdate(() => windows.Count == 0);
            Assert.True(WallpaperProfiles.Engine.DesktopCanvasProcess.IsRunning(store.FilePath));
            Assert.Equal("Study only", store.Load().Widgets.Single(widget => widget.Id == studyWidget.Id).Content);
            store.Update(canvas => { canvas.ActiveProfileId = studyId; canvas.Widgets.First(widget => widget.Id == generalId).ProfileId = null; canvas.Widgets.RemoveAll(widget => widget.Id != generalId); });
            AwaitUpdate(() => windows.Count == 1 && windows.ContainsKey(generalId));
            var running = Assert.Single(windows.Values);
            ((TextBox)view.FindName("WidgetTitle")).Text = "Updated project";
            ((TextBox)view.FindName("WidgetContent")).Text = "Changes saved while the widget is running.";
            Press("StylePaperButton");
            ((CheckBox)view.FindName("WidgetGlassEffect")).IsChecked = false;
            ((Slider)view.FindName("WidgetFontSize")).Value = 26;
            ((Slider)view.FindName("WidgetWidth")).Value = 420;
            Press("SaveWidgetButton");
            AwaitUpdate(() => running.Title == "Belie widget · Updated project");
            Assert.Same(running, Assert.Single(windows.Values));
            var liveBody = (ContentControl)typeof(WallpaperProfiles.Engine.DesktopWidgetWindow)
                .GetField("_body", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(running)!;
            var liveText = (TextBlock)((ScrollViewer)liveBody.Content).Content;
            Assert.Equal("Changes saved while the widget is running.", liveText.Text);
            Assert.Equal(26, liveText.FontSize);
            Assert.Equal("Georgia", liveText.FontFamily.Source);
            Assert.Equal("#FF282A25", ((SolidColorBrush)liveText.Foreground).Color.ToString());
            Assert.InRange(running.Width, 419, 422);
            var liveGlass = (Grid)typeof(WallpaperProfiles.Engine.DesktopWidgetWindow)
                .GetField("_glass", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(running)!;
            Assert.Equal(Visibility.Collapsed, liveGlass.Visibility);
            var liveGrip = (System.Windows.Controls.Primitives.Thumb)typeof(WallpaperProfiles.Engine.DesktopWidgetWindow)
                .GetField("_resize", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(running)!;
            liveGrip.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0)
                { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent });
            ((TextBox)view.FindName("WidgetTitle")).Text = "Saved during resize";
            Press("SaveWidgetButton");
            liveGrip.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, 0, false)
                { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
            AwaitUpdate(() => running.Title == "Belie widget · Saved during resize");
            Press("StyleGlassButton");
            ((TextBox)view.FindName("WidgetBorderColor")).Text = "#BFA3EE";
            Press("SaveWidgetButton");
            AwaitUpdate(() => ((Border)running.Content).BorderBrush is LinearGradientBrush glassRim
                && glassRim.GradientStops[0].Color == Color.FromArgb(190, 191, 163, 238));
            ((CheckBox)view.FindName("WidgetGlassEffect")).IsChecked = false;
            Press("StyleMinimalButton");
            Assert.False(((CheckBox)view.FindName("WidgetBackground")).IsChecked);
            ((CheckBox)view.FindName("WidgetGlassEffect")).IsChecked = true;
            Assert.True(((CheckBox)view.FindName("WidgetBackground")).IsChecked);
            foreach (var preset in new[] { "Paper", "Minimal", "Cathedral", "Crimson", "Parchment", "Royal", "Neon", "Terminal", "Gothic" })
            {
                Press("Style" + preset + "Button");
                Press("SaveWidgetButton");
                var appearance = Assert.Single(store.Load().Widgets);
                AwaitUpdate(() => ((TextBlock)((ScrollViewer)liveBody.Content).Content).FontFamily.Source == appearance.FontFamily
                    && ((SolidColorBrush)((TextBlock)((ScrollViewer)liveBody.Content).Content).Foreground).Color.ToString() == "#FF" + appearance.TextColor[1..]
                    && liveGlass.Visibility == Visibility.Visible);
                Assert.True(appearance.GlassEffect);
                Assert.True(appearance.ShowBackground);
                var styledText = (TextBlock)((ScrollViewer)liveBody.Content).Content;
                Assert.Equal("#FF" + appearance.TextColor[1..], ((SolidColorBrush)styledText.Foreground).Color.ToString());
                Assert.Equal(appearance.CornerRadius, ((Border)running.Content).CornerRadius.TopLeft);
                var savedTextColor = appearance.TextColor;
                ((CheckBox)view.FindName("WidgetGlassEffect")).IsChecked = false;
                Press("SaveWidgetButton");
                AwaitUpdate(() => liveGlass.Visibility == Visibility.Collapsed);
                Assert.False(Assert.Single(store.Load().Widgets).GlassEffect);
                Assert.Equal(savedTextColor, Assert.Single(store.Load().Widgets).TextColor);
                ((CheckBox)view.FindName("WidgetGlassEffect")).IsChecked = true;
                Press("SaveWidgetButton");
                AwaitUpdate(() => liveGlass.Visibility == Visibility.Visible);
            }
            RenderWorkspace(running, "canvas-gothic-glass", 420, 230);
            ((ListBox)view.FindName("WidgetList")).SelectedIndex = -1;
            ((ListBox)view.FindName("WidgetList")).SelectedIndex = 0;
            Assert.True(((CheckBox)view.FindName("WidgetGlassEffect")).IsChecked);
            ((CheckBox)view.FindName("WidgetGlassEffect")).IsChecked = false;
            ((TextBox)view.FindName("WidgetTitle")).Text = "   ";
            ((CheckBox)view.FindName("WidgetItalic")).IsChecked = true;
            ((TextBox)view.FindName("WidgetBorderColor")).Text = "#8956B4";
            ((Slider)view.FindName("WidgetBorderWidth")).Value = 2.5;
            Press("SaveWidgetButton");
            AwaitUpdate(() => running.Title == "Belie widget · Untitled note");
            var untitled = Assert.Single(store.Load().Widgets);
            Assert.Equal("", untitled.Title);
            Assert.Equal("", untitled.Duplicate().Title);
            Assert.True(untitled.Italic);
            Assert.Equal("#8956B4", untitled.BorderColor);
            Assert.Equal(2.5, untitled.BorderWidth);
            var liveHeader = (Border)typeof(WallpaperProfiles.Engine.DesktopWidgetWindow)
                .GetField("_header", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(running)!;
            var liveCard = (Border)running.Content;
            Assert.Equal(Visibility.Collapsed, liveHeader.Visibility);
            Assert.Equal(new Thickness(2.5), liveCard.BorderThickness);
            Assert.Equal("#FF8956B4", ((SolidColorBrush)liveCard.BorderBrush).Color.ToString());
            Assert.Equal(FontStyles.Italic, ((TextBlock)((ScrollViewer)liveBody.Content).Content).FontStyle);
            ((TextBox)view.FindName("WidgetTitle")).Text = "A title again";
            Press("SaveWidgetButton");
            AwaitUpdate(() => running.Title == "Belie widget · A title again");
            Assert.Equal(Visibility.Visible, liveHeader.Visibility);
        }
        Press("AddCountdownButton");
        ((TextBox)view.FindName("WidgetTitle")).Text = "";
        ((TextBox)view.FindName("WidgetTime")).Text = "bad time";
        Press("SaveWidgetButton");
        Assert.Single(store.Load().Widgets);
        Assert.Contains("HH:mm", ((TextBlock)view.FindName("CanvasMessage")).Text);
        ((TextBox)view.FindName("WidgetTime")).Text = "18:30";
        Press("SaveWidgetButton");
        Press("AddLinkButton");
        ((TextBox)view.FindName("WidgetTitle")).Text = "";
        ((TextBox)view.FindName("WidgetContent")).Text = "file:///C:/Windows/notepad.exe";
        Press("SaveWidgetButton");
        Assert.Equal(2, store.Load().Widgets.Count);
        ((TextBox)view.FindName("WidgetContent")).Text = "https://example.com/project";
        Press("SaveWidgetButton");
        Press("AddImageButton");
        ((TextBox)view.FindName("WidgetTitle")).Text = "";
        ((TextBox)view.FindName("WidgetContent")).Text = imagePath;
        ((CheckBox)view.FindName("WidgetLocked")).IsChecked = true;
        Press("SaveWidgetButton");
        Assert.Equal(4, store.Load().Widgets.Count);
        Assert.True(store.Load().Widgets.Last().Locked);
        Press("HideWidgetsButton");
        Assert.False(store.Load().Enabled);
        Assert.Equal(4, store.Load().Widgets.Count);
        Press("ShowWidgetsButton");
        Assert.True(store.Load().Enabled);
        Press("FindWidgetsButton");
        Assert.Equal(4, store.Load().Widgets.Count);
        Press("DeleteWidgetButton");
        Assert.Equal(3, store.Load().Widgets.Count);
        Press("AddSketchButton");
        ((TextBox)view.FindName("WidgetTitle")).Text = "";
        Press("SaveWidgetButton");
        var sketch = store.Load().Widgets.Last();
        Assert.Equal(DesktopWidgetKind.Sketch, sketch.Kind);
        using (var sketchWindow = new DisposableWidget(new WallpaperProfiles.Engine.DesktopWidgetWindow(sketch, store)))
        {
            var sketchSurface = (Grid)((Border)sketchWindow.Window.Content).Child;
            var sketchGrid = (Grid)sketchSurface.Children[1];
            var sketchBody = sketchGrid.Children.OfType<ContentControl>().Single();
            var drawing = (WallpaperProfiles.Engine.DesktopSketch)sketchBody.Content;
            var ink = drawing.Children.OfType<InkCanvas>().Single();
            ink.Strokes.Add(new System.Windows.Ink.Stroke(new System.Windows.Input.StylusPointCollection(new[] {
                new System.Windows.Input.StylusPoint(20, 20), new System.Windows.Input.StylusPoint(120, 80) })));
            var savedInk = store.Load().Widgets.Last().Drawing;
            Assert.NotEmpty(savedInk);
            using var stream = new MemoryStream(Convert.FromBase64String(savedInk));
            Assert.Single(new System.Windows.Ink.StrokeCollection(stream));
            sketchWindow.Window.Update(store.Load().Widgets.Last());
            Assert.Same(drawing, sketchBody.Content);
            RenderWorkspace(sketchWindow.Window, "canvas-sketch", 420, 300);
        }
        var noteDrop = new System.Windows.DataObject(System.Windows.DataFormats.UnicodeText, "A dropped idea");
        view.CreateFromDrop(noteDrop);
        Assert.Equal("A dropped idea", store.Load().Widgets.Last().Content);
        var linkDrop = new System.Windows.DataObject(System.Windows.DataFormats.UnicodeText, "https://example.com/study-notes");
        var droppedLink = WallpaperProfiles.Engine.DesktopCanvasFeatures.ReadDrop(linkDrop).Single();
        Assert.Equal(DesktopWidgetKind.Link, droppedLink.Kind);
        var filesDrop = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { imagePath });
        var droppedImages = WallpaperProfiles.Engine.DesktopCanvasFeatures.ReadDrop(filesDrop);
        view.CreateFromDrop(filesDrop);
        Assert.NotEqual(imagePath, store.Load().Widgets.Last().Content);
        Assert.True(File.Exists(store.Load().Widgets.Last().Content));
        var invalidDrop = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { "missing.exe" });
        Assert.Throws<InvalidOperationException>(() => WallpaperProfiles.Engine.DesktopCanvasFeatures.ReadDrop(invalidDrop));
        Assert.Equal(6, store.Load().Widgets.Count);
        for (var extra = 0; extra < 3; extra++)
        {
            ((ListBox)view.FindName("WidgetList")).SelectedIndex = store.Load().Widgets.Count - 1;
            Press("DeleteWidgetButton");
        }
        Assert.Equal(3, store.Load().Widgets.Count);
        ((ListBox)view.FindName("WidgetList")).SelectedIndex = 0;
        ((ScrollViewer)view.FindName("WidgetEditorScroll")).ScrollToTop(); main.UpdateLayout();
        Assert.Null(view.FindName("BoardList"));
        RenderWorkspace(main, "canvas-widgets", 1040, 790);
        RenderWorkspace(main, "canvas-widgets-compact", 880, 650);
        Press("CloseWidgetEditorButton");
        Assert.Null(((ListBox)view.FindName("WidgetList")).SelectedItem);
        Assert.Null(previewBox.Child);
        RenderWorkspace(main, "canvas-gallery", 1040, 790);
        var overview = (FrameworkElement)view.FindName("DesktopOverview");
        Assert.Equal(Visibility.Visible, overview.Visibility);
        Assert.True(overview.ActualWidth > 300);
        var displayedWidget = store.Load().Widgets.First();
        var desktopItem = VisualChildren(overview).OfType<Border>().Single(item => Equals(item.ToolTip, displayedWidget.DisplayTitle));
        var desktopScale = WallpaperProfiles.Engine.DesktopWidgetWindow.PreviewScale(displayedWidget);
        Assert.Equal(Math.Ceiling(displayedWidget.Width * desktopScale.M11), desktopItem.Width);
        Assert.Equal(Math.Ceiling(displayedWidget.Height * desktopScale.M22), desktopItem.Height);
        Assert.IsType<Viewbox>(desktopItem.Child);
        var widgetList = (ListBox)view.FindName("WidgetList");
        Button VisibilityButton()
        {
            main.UpdateLayout();
            var container = (DependencyObject)widgetList.ItemContainerGenerator.ContainerFromIndex(0);
            return VisualChildren(container).OfType<Button>().Single(button => button.Name == "CardVisibilityButton");
        }
        var firstWidget = store.Load().Widgets[0];
        store.UpdateWidget(firstWidget.Id, saved => saved.Content = "Latest content from another window");
        VisibilityButton().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(store.Load().Widgets[0].Enabled);
        Assert.Equal("Latest content from another window", store.Load().Widgets[0].Content);
        VisibilityButton().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(store.Load().Widgets[0].Enabled);
        Assert.Equal(3, store.Load().Widgets.Count);
        widgetList.SelectedIndex = 0;
        Assert.Equal("Latest content from another window", ((TextBox)view.FindName("WidgetContent")).Text);
        Click(main, "ProfilesNavButton");
        Assert.Equal(Visibility.Collapsed, pane.Visibility);
        Click(main, "CanvasNavButton");
        Assert.Same(view, pane.Content);
        Click(main, "DashboardNavButton");
        Assert.Equal(Visibility.Collapsed, pane.Visibility);
    }

    private sealed class DisposableWidget : IDisposable
    {
        public WallpaperProfiles.Engine.DesktopWidgetWindow Window { get; }
        public DisposableWidget(WallpaperProfiles.Engine.DesktopWidgetWindow window) => Window = window;
        public void Dispose() => Window.Close();
    }

    private static void CheckActivityAndSceneControls(MainWindow main, WallpaperCoordinator coordinator, TestClock clock,
        WallpaperProfile profile, string imagePath)
    {
        profile.SceneAccent = ScenePresets.Create("Focus").SceneAccent;
        coordinator.SwitchManually(profile.Id);
        Click(main, "DashboardNavButton");
        Assert.Equal(Visibility.Visible, ((FrameworkElement)main.FindName("DashboardPane")).Visibility);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)main.FindName("ProfilePane")).Visibility);
        Assert.Equal("Focus", ((TextBlock)main.FindName("ActiveProfileText")).Text);
        Assert.Equal("Manual choice", ((TextBlock)main.FindName("ActivationReasonText")).Text);
        Assert.True(WallpaperProfiles.Engine.SceneController.TryParseAccent(profile.SceneAccent, out var color));
        Assert.Equal(color, (System.Windows.Media.Color)System.Windows.Application.Current.Resources["SceneAccentColor"]);
        main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        Assert.Equal(color, ((SolidColorBrush)main.FindResource("AccentBrush")).Color);
        Click(main, "StartOverrideButton");
        Assert.True(coordinator.GetActivitySnapshot().ManualOverride?.IsTimed);
        Assert.Contains("60 min left", ((TextBlock)main.FindName("OverrideStatusText")).Text);
        Click(main, "PauseAutomationButton");
        Assert.Equal("Paused", ((TextBlock)main.FindName("AutomationModeText")).Text);
        Click(main, "ResumeAutomaticButton");
        Assert.False(coordinator.Paused);
        Assert.Null(coordinator.GetActivitySnapshot().ManualOverride);
        Assert.Equal("Running", ((TextBlock)main.FindName("AutomationModeText")).Text);
        Click(main, "StartOverrideButton");
        clock.Advance(TimeSpan.FromHours(1));
        coordinator.CheckNow();
        Assert.Equal("No manual override is active.", ((TextBlock)main.FindName("OverrideStatusText")).Text);
        Assert.NotEmpty(((ItemsControl)main.FindName("RecentActivityList")).Items);
        RenderWorkspace(main, "activity", 1040, 790);
        RenderWorkspace(main, "activity-compact", 880, 650);
        var dashboard = (ScrollViewer)main.FindName("DashboardPane");
        var settingsButton = (Button)main.FindName("SettingsButton");
        var root = (FrameworkElement)main.Content;
        var settingsPosition = settingsButton.TranslatePoint(new System.Windows.Point(), root);
        Assert.True(settingsButton.IsVisible);
        Assert.InRange(settingsPosition.Y + settingsButton.ActualHeight, 1, root.ActualHeight);
        Assert.InRange(dashboard.ViewportHeight, 1, root.ActualHeight - 47);
        Assert.True(dashboard.ScrollableHeight > 0);
        dashboard.ScrollToTop(); main.UpdateLayout();
        var recentActivity = (ItemsControl)main.FindName("RecentActivityList");
        var wheel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120)
            { RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent };
        recentActivity.RaiseEvent(wheel); main.UpdateLayout();
        Assert.True(wheel.Handled);
        Assert.True(dashboard.VerticalOffset > 0);
        dashboard.ScrollToEnd(); main.UpdateLayout();
        Assert.Equal(dashboard.ScrollableHeight, dashboard.VerticalOffset, precision: 1);
        var lastActivity = (FrameworkElement)recentActivity.ItemContainerGenerator.ContainerFromIndex(recentActivity.Items.Count - 1);
        var lastPosition = lastActivity.TranslatePoint(new System.Windows.Point(), dashboard);
        Assert.InRange(lastPosition.Y + lastActivity.ActualHeight, 1, dashboard.ActualHeight);
        Click(main, "SettingsButton");
        Assert.True(((System.Windows.Controls.Primitives.Popup)main.FindName("SettingsPopup")).IsOpen);
        Click(main, "SettingsButton");
        RenderWorkspace(main, "activity-scrolled", 880, 650);
        dashboard.ScrollToTop(); main.UpdateLayout();

        var editor = new ProfileEditorWindow(new WallpaperProfile { FolderPath = imagePath }, isNew: true)
            { ShowInTaskbar = false, Opacity = 0 };
        editor.Loaded += (_, _) =>
        {
            ((ComboBox)editor.FindName("PresetCombo")).SelectedItem = "Gaming";
            Assert.Equal("Gaming", ((TextBox)editor.FindName("NameBox")).Text);
            Assert.Equal("#ADBDF4", ((TextBox)editor.FindName("SceneAccentBox")).Text);
            Assert.True(((CheckBox)editor.FindName("AmbientMuteBox")).IsChecked);
            Assert.Equal(15, ((Slider)editor.FindName("AmbientVolumeSlider")).Value);
            ((TextBox)editor.FindName("AudioPathBox")).Text = Path.Combine(Path.GetDirectoryName(imagePath)!, "ambient.wav");
            ((TabControl)editor.FindName("EditorTabs")).SelectedIndex = 1;
            RenderWorkspace(editor, "scene-editor", 680, 720);
            typeof(ProfileEditorWindow).GetMethod("Save_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(editor, new object[] { editor, new RoutedEventArgs() });
        };
        Assert.True(editor.ShowDialog());
        Assert.Equal("#ADBDF4", editor.Result?.SceneAccent);
        Assert.Equal(15, editor.Result?.AmbientVolume);
        Assert.True(editor.Result?.AmbientMuted);
        Assert.EndsWith("ambient.wav", editor.Result?.AmbientAudioPath);
    }

    private static void CheckPreviewScaling(DesktopCanvasStore store, string imagePath)
    {
        var type = typeof(WallpaperProfiles.Engine.DesktopWidgetWindow);
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
            var renderer = new WallpaperProfiles.Engine.DesktopWidgetWindow(widget, store);
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
                    Assert.Equal((320 + 48) * factor / screen.Bounds.Width, brush.Viewbox.Width, precision: 6);
                    Assert.Equal((200 + 48) * factor / screen.Bounds.Height, brush.Viewbox.Height, precision: 6);
                    Assert.Equal((bounds.X - 24 * factor - screen.Bounds.Left) / screen.Bounds.Width, brush.Viewbox.X, precision: 6);
                    Assert.Equal((bounds.Y - 24 * factor - screen.Bounds.Top) / screen.Bounds.Height, brush.Viewbox.Y, precision: 6);
                }
                Assert.Equal(screen.WorkingArea.Right - 10, widget.X);
                Assert.Equal(screen.WorkingArea.Bottom - 10, widget.Y);
                Assert.Equal(savedCanvas, File.ReadAllText(store.FilePath));
            }
            finally { renderer.Close(); }
        }
        finally { for (var i = 0; i < fields.Length; i++) fields[i].SetValue(null, originals[i]); }
    }

    private static void RenderWorkspace(Window window, string view, int width, int height)
    {
        if (window is MainWindow) { window.Width = width; window.Height = height; window.UpdateLayout(); }
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
