using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace WallpaperProfiles.UI;

internal enum DraftChoice { Save, Discard, Cancel }

internal static class DraftGuard
{
    internal static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    internal static string Snapshot(object value) => JsonSerializer.Serialize(value);
    internal static void Focus(System.Windows.Controls.Control field)
    {
        for (DependencyObject? parent = field; parent != null; parent = LogicalTreeHelper.GetParent(parent))
            if (parent is Expander expander) expander.IsExpanded = true;
        field.Focus(); field.BringIntoView();
    }

    internal static bool Confirm(Window? owner, string subject, Func<bool> save, Action discard,
        Func<DraftChoice>? choose = null)
    {
        var choice = choose?.Invoke() ?? Ask(owner, subject);
        if (choice == DraftChoice.Cancel) return false;
        if (choice == DraftChoice.Save) return save();
        discard();
        return true;
    }

    private static DraftChoice Ask(Window? owner, string subject)
    {
        var result = DraftChoice.Cancel;
        var dialog = new Window { Title = "Unsaved changes", Width = 430, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner, ShowInTaskbar = false, FontSize = 14 };
        dialog.SetResourceReference(Window.BackgroundProperty, "BgBrush");
        dialog.SetResourceReference(Window.ForegroundProperty, "TextPrimaryBrush");
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = "Save your changes?", FontSize = 22, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = $"You have unsaved changes to {subject}.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 24) });
        var commands = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (label, choice) in new[] { ("Save", DraftChoice.Save), ("Discard", DraftChoice.Discard), ("Cancel", DraftChoice.Cancel) })
        {
            var button = new Button { Content = label, MinWidth = 82, MinHeight = 36, Margin = new Thickness(8, 0, 0, 0), IsCancel = choice == DraftChoice.Cancel };
            if (choice == DraftChoice.Save) button.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
            button.Click += (_, _) => { result = choice; dialog.DialogResult = choice != DraftChoice.Cancel; };
            commands.Children.Add(button);
        }
        content.Children.Add(commands); dialog.Content = content;
        dialog.ShowDialog();
        return result;
    }
}
