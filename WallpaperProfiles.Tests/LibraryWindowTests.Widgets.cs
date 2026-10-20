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
    private static void CheckDesktopCanvasPanel(MainWindow main, string dir, string imagePath)
    {
        var store = new DesktopCanvasStore(Path.Combine(dir, "canvas.json"));
        var starts = 0;
        var widgetProfiles = new List<WallpaperProfile> { new() { Name = "Study" }, new() { Name = "Chill" } };
        var view = new DesktopCanvasView(store, () => starts++, profiles: () => widgetProfiles) { ChooseDraftAction = () => DraftChoice.Discard, ConfirmDeleteAction = () => true };
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
        var visiblePreview = previewBox.IsVisible ? (FrameworkElement)previewBox
            : (Border)typeof(DesktopCanvasView).GetField("_overviewFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        var previewLocation = visiblePreview.TranslatePoint(new System.Windows.Point(0, 0), (UIElement)main.Content);
        Assert.True(visiblePreview.ActualHeight > 0);
        Assert.InRange(previewLocation.Y, 0, ((FrameworkElement)main.Content).ActualHeight);
        Assert.InRange(previewLocation.Y + visiblePreview.ActualHeight, 1, ((FrameworkElement)main.Content).ActualHeight);
        var pointer = new System.Drawing.Point(500, 500);
        var widget = new DesktopWidgetWindow(Assert.Single(store.Load().Widgets), store, () => pointer);
        RenderWorkspace(widget, "canvas-note", 300, 230);
        widget.Opacity = 0;
        widget.Show();
        var initialWidth = widget.Width;
        var initialHeight = widget.Height;
        var grip = (System.Windows.Controls.Primitives.Thumb)typeof(DesktopWidgetWindow)
            .GetField("_resize", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(widget)!;
        grip.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0)
            { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent });
        var scale = PresentationSource.FromVisual(widget)!.CompositionTarget!.TransformToDevice;
        var body = (ContentControl)typeof(DesktopWidgetWindow)
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
        var glassWidget = new DesktopWidgetWindow(Assert.Single(store.Load().Widgets), store);
        RenderWorkspace(glassWidget, "canvas-glass", 350, 230);
        glassWidget.Close();
        using (var host = new WallpaperProfiles.Engine.DesktopCanvasHost(store.FilePath,
            saved => new DesktopWidgetWindow(saved, store) { Opacity = 0 }))
        {
            var windows = (Dictionary<Guid, DesktopWidgetWindow>)typeof(WallpaperProfiles.Engine.DesktopCanvasHost)
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
            var liveBody = (ContentControl)typeof(DesktopWidgetWindow)
                .GetField("_body", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(running)!;
            var liveText = (TextBlock)((ScrollViewer)liveBody.Content).Content;
            Assert.Equal("Changes saved while the widget is running.", liveText.Text);
            Assert.Equal(26, liveText.FontSize);
            Assert.Equal("Georgia", liveText.FontFamily.Source);
            Assert.Equal("#FF282A25", ((SolidColorBrush)liveText.Foreground).Color.ToString());
            Assert.InRange(running.Width, 419, 422);
            var liveGlass = (Grid)typeof(DesktopWidgetWindow)
                .GetField("_glass", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(running)!;
            Assert.Equal(Visibility.Collapsed, liveGlass.Visibility);
            var liveGrip = (System.Windows.Controls.Primitives.Thumb)typeof(DesktopWidgetWindow)
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
            var liveHeader = (Border)typeof(DesktopWidgetWindow)
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
        using (var sketchWindow = new DisposableWidget(new DesktopWidgetWindow(sketch, store)))
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
        var desktopScale = DesktopWidgetWindow.PreviewScale(displayedWidget);
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
        public DesktopWidgetWindow Window { get; }
        public DisposableWidget(DesktopWidgetWindow window) => Window = window;
        public void Dispose() => Window.Close();
    }
}
