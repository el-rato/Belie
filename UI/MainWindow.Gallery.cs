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

        FilterRail(); _shellCompact = null; UpdateShellLayout();
    }

    private void AddSceneCard(WallpaperProfile profile, MonitorThumbnail monitor)
    {
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var artwork = new Grid { Height = 118 };
        var fallback = new Border { CornerRadius = new CornerRadius(13, 13, 0, 0),
            Background = new LinearGradientBrush(System.Windows.Media.Color.FromRgb(29, 82, 80),
                System.Windows.Media.Color.FromRgb(150, 177, 137), 35) };
        artwork.Children.Add(fallback);
        var image = new ImageBrush { Stretch = Stretch.UniformToFill };
        BindingOperations.SetBinding(image, ImageBrush.ImageSourceProperty,
            new System.Windows.Data.Binding(nameof(MonitorThumbnail.ScreenSource)) { Source = monitor });
        artwork.Children.Add(new Border { CornerRadius = new CornerRadius(13, 13, 0, 0), Background = image });
        var placeholder = new TextBlock { Text = "Add a wallpaper  →", Foreground = System.Windows.Media.Brushes.White,
            FontSize = 12, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center, IsHitTestVisible = false };
        var placeholderStyle = new Style(typeof(TextBlock));
        placeholderStyle.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        var empty = new DataTrigger { Binding = new System.Windows.Data.Binding(nameof(MonitorThumbnail.ScreenSource)) { Source = monitor }, Value = null };
        empty.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible));
        placeholderStyle.Triggers.Add(empty); placeholder.Style = placeholderStyle;
        artwork.Children.Add(placeholder);
        var preview = new System.Windows.Controls.Button { Content = artwork, Tag = profile.Id,
            Style = (Style)FindResource("ScenePreviewButton"), ToolTip = $"Open {profile.Name} in the wallpaper studio" };
        preview.Click += (_, _) => Select(profile.Id);
        System.Windows.Automation.AutomationProperties.SetName(preview, $"Preview {profile.Name}");
        content.Children.Add(preview);
        var footer = new Grid { Margin = new Thickness(13) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = profile.Name, FontWeight = FontWeights.SemiBold,
            FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0), ToolTip = profile.Name };
        footer.Children.Add(title);
        var play = new System.Windows.Controls.Button { Padding = new Thickness(10, 6, 10, 6),
            FontSize = 12, ToolTip = $"Apply {profile.Name} to your desktop", Tag = profile.Id };
        var style = new Style(typeof(System.Windows.Controls.Button), (Style)FindResource("PrimaryButton"));
        var active = new DataTrigger { Binding = new System.Windows.Data.Binding(nameof(MonitorThumbnail.IsActive)) { Source = monitor }, Value = true };
        active.Setters.Add(new Setter(System.Windows.Controls.ContentControl.ContentProperty, "Applied"));
        style.Triggers.Add(active);
        style.Setters.Add(new Setter(System.Windows.Controls.ContentControl.ContentProperty, "Apply"));
        play.Style = style;
        play.Click += (_, _) => _coordinator.SwitchManually(profile.Id);
        System.Windows.Automation.AutomationProperties.SetName(play, $"Apply profile {profile.Name}");
        Grid.SetColumn(play, 1); footer.Children.Add(play);
        Grid.SetRow(footer, 1); content.Children.Add(footer);
        SceneGallery.Children.Add(new Border { Width = 214, Margin = new Thickness(0, 0, 12, 0),
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
