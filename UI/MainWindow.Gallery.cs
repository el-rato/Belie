using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.UI;

internal partial class MainWindow
{
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (RailStack != null && SearchEmpty != null)
        {
            FilterRail();
        }
    }

    private void SceneGallery_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // The horizontal gallery otherwise consumes the page's vertical wheel input.
        DashboardPane.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = Mouse.MouseWheelEvent,
            Source = DashboardPane
        });
        e.Handled = true;
    }

    private void FilterRail()
    {
        var query = SearchBox.Text.Trim();
        var visible = 0;
        foreach (var entry in _railEntries)
        {
            var matches = entry.Monitor.ProfileName.Contains(query, StringComparison.CurrentCultureIgnoreCase);
            entry.Card.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
            if (matches) visible++;
        }
        SearchEmpty.Visibility = visible == 0 && query.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ============ Rail ============

    private void BuildRail()
    {
        RailStack.Children.Clear();
        SceneGallery.Children.Clear();
        _railEntries.Clear();
        SceneGalleryEmpty.Visibility = _coordinator.Profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_coordinator.Profiles.Count == 0)
        {
            RailStack.Children.Add(new TextBlock
            {
                Text = "No profiles yet.\nAdd one below.",
                Foreground = ThemeBrush("TextTertiaryBrush"),
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 10),
                Opacity = 0.9,
            });
        }

        foreach (var profile in _coordinator.Profiles)
        {
            var monitor = new MonitorThumbnail
            {
                Width = 40,
                BezelWidth = 40,
                BezelHeight = 40,
                ShowName = false,
                ProfileName = profile.Name,
                HasRules = profile.Schedule.Count > 0 || profile.EventTriggers.Count > 0,
                IsActive = profile.Id == _coordinator.ActiveProfileId,
            };

            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(monitor);
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(new TextBlock { Text = profile.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var status = new TextBlock { Text = profile.Id == _coordinator.ActiveProfileId ? "Applied"
                : profile.Schedule.Count + profile.EventTriggers.Count > 0 ? "Automated" : "", FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
            status.SetResourceReference(TextBlock.ForegroundProperty, profile.Id == _coordinator.ActiveProfileId ? "AccentTextBrush" : "TextSecondaryBrush");
            labels.Children.Add(status); Grid.SetColumn(labels, 1); row.Children.Add(labels);
            var card = new ProfileNavigationCard
            {
                Open = () => Select(profile.Id),
                Margin = new Thickness(0, 0, 0, 6),
                Style = (Style)FindResource("ProfileCard"),
                Focusable = true,
                Child = row,
                ToolTip = profile.Name + (profile.Id == _coordinator.ActiveProfileId ? " · Applied" : " · Not applied") + (profile.Schedule.Count + profile.EventTriggers.Count > 0 ? " · Automated" : ""),
            };
            System.Windows.Automation.AutomationProperties.SetName(card, $"Open profile {profile.Name}");
            card.MouseLeftButtonUp += (_, _) => Select(profile.Id);
            card.KeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Enter or Key.Space)) return;
                Select(profile.Id);
                e.Handled = true;
            };
            RailStack.Children.Add(card);
            _railEntries.Add(new RailEntry(profile.Id, card, monitor));
            AddSceneCard(profile, monitor);

            _ = LoadRailThumbAsync(profile, monitor);
        }

        SceneGalleryCount.Text = _coordinator.Profiles.Count.ToString();
        FilterRail(); _shellCompact = null; UpdateShellLayout();
    }

    private void AddSceneCard(WallpaperProfile profile, MonitorThumbnail monitor)
    {
        // The wallpaper fills the whole card; the name and the apply pill sit on a soft shade at the bottom.
        var content = new Grid { Height = 150 };
        RoundedClip.SetRadius(content, 13);
        var artwork = new Grid();
        artwork.Children.Add(new Border { Background = new LinearGradientBrush(System.Windows.Media.Color.FromRgb(29, 82, 80),
            System.Windows.Media.Color.FromRgb(150, 177, 137), 35) });
        var image = new ImageBrush { Stretch = Stretch.UniformToFill };
        BindingOperations.SetBinding(image, ImageBrush.ImageSourceProperty,
            new System.Windows.Data.Binding(nameof(MonitorThumbnail.ScreenSource)) { Source = monitor });
        artwork.Children.Add(new Border { Background = image });
        var placeholder = new TextBlock { Text = "Add a wallpaper  →", Foreground = System.Windows.Media.Brushes.White,
            FontSize = 12, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 40), IsHitTestVisible = false };
        var placeholderStyle = new Style(typeof(TextBlock));
        placeholderStyle.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        var empty = new DataTrigger { Binding = new System.Windows.Data.Binding(nameof(MonitorThumbnail.ScreenSource)) { Source = monitor }, Value = null };
        empty.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible));
        placeholderStyle.Triggers.Add(empty); placeholder.Style = placeholderStyle;
        artwork.Children.Add(placeholder);
        var preview = new System.Windows.Controls.Button { Content = artwork, Tag = profile.Id,
            Style = (Style)FindResource("ScenePreviewButton"), ToolTip = $"Open {profile.Name}" };
        preview.Click += (_, _) => Select(profile.Id);
        System.Windows.Automation.AutomationProperties.SetName(preview, $"Preview {profile.Name}");
        content.Children.Add(preview);
        var shade = new Border { IsHitTestVisible = false, Background = new LinearGradientBrush(new GradientStopCollection
        {
            new(System.Windows.Media.Color.FromArgb(0, 0, 0, 0), .35), new(System.Windows.Media.Color.FromArgb(215, 0, 0, 0), 1)
        }, 90) };
        content.Children.Add(shade);
        var footer = new Grid { Margin = new Thickness(12, 0, 10, 10), VerticalAlignment = VerticalAlignment.Bottom };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = profile.Name, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White,
            FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0), ToolTip = profile.Name, IsHitTestVisible = false };
        footer.Children.Add(title);
        var play = new System.Windows.Controls.Button { ToolTip = $"Apply {profile.Name} to your desktop", Tag = profile.Id };
        // Waiting profiles get a frosted pill; the applied one turns to the accent colour.
        var style = new Style(typeof(System.Windows.Controls.Button), (Style)FindResource("PillButton"));
        style.Setters.Add(new Setter(System.Windows.Controls.ContentControl.ContentProperty, "Apply"));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, new SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 255, 255, 255))));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.BorderBrushProperty, new SolidColorBrush(System.Windows.Media.Color.FromArgb(70, 255, 255, 255))));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, System.Windows.Media.Brushes.White));
        var active = new DataTrigger { Binding = new System.Windows.Data.Binding(nameof(MonitorThumbnail.IsActive)) { Source = monitor }, Value = true };
        active.Setters.Add(new Setter(System.Windows.Controls.ContentControl.ContentProperty, "Applied"));
        active.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, new DynamicResourceExtension("AccentBrush")));
        active.Setters.Add(new Setter(System.Windows.Controls.Control.BorderBrushProperty, new DynamicResourceExtension("AccentBrush")));
        active.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 24, 29))));
        style.Triggers.Add(active);
        play.Style = style;
        play.Click += (_, _) => _coordinator.SwitchManually(profile.Id);
        System.Windows.Automation.AutomationProperties.SetName(play, $"Apply profile {profile.Name}");
        Grid.SetColumn(play, 1); footer.Children.Add(play);
        content.Children.Add(footer);
        SceneGallery.Children.Add(new Border { Width = 230, Margin = new Thickness(0, 0, 12, 0),
            Style = (Style)FindResource("SceneCard"), Child = content });
    }

    private static async Task LoadRailThumbAsync(WallpaperProfile profile, MonitorThumbnail monitor)
    {
        var preview = await Task.Run(() =>
        {
            var first = WallpaperEngine.GetProfileMedia(profile).FirstOrDefault();
            return (Path: first, Image: ThumbnailLoader.Load(first, 320));
        });
        if (monitor.Dispatcher.HasShutdownStarted) return;
        await monitor.Dispatcher.InvokeAsync(() =>
        {
            monitor.IsVideo = preview.Path != null && WallpaperEngine.IsLiveFile(preview.Path);
            monitor.ScreenSource = preview.Image;
        });
    }
}
