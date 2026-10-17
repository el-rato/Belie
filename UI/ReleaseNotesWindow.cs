using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;

namespace WallpaperProfiles.UI;

internal sealed class ReleaseNotesWindow : Window
{
    private static readonly Lazy<string> Bundled = new(() =>
    {
        using var stream = typeof(ReleaseNotesWindow).Assembly.GetManifestResourceStream("Belie.ReleaseNotes.md")
            ?? throw new InvalidOperationException("The bundled patch notes could not be found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    internal static string BundledNotes => Bundled.Value;

    internal static string? VersionNotes(string version) => ExtractVersion(BundledNotes, version);

    internal static string? ExtractVersion(string markdown, string version)
    {
        var match = Regex.Match(markdown, @"^## \[" + Regex.Escape(version) + @"\][^\r\n]*\r?\n.*?(?=^## \[|\z)",
            RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return match.Success ? match.Value.Trim() : null;
    }

    internal static string UpdateNotes(string version, string? suppliedNotes) =>
        !string.IsNullOrWhiteSpace(suppliedNotes) ? suppliedNotes
            : VersionNotes(version) ?? "Detailed notes were not included with this update. Choose View on GitHub to read the release page.";

    public ReleaseNotesWindow(string version, string markdown, bool updateAvailable = false)
    {
        Title = updateAvailable ? "Release notes · Belie " + version : "What's new · Belie " + version;
        Width = 700; Height = 740; MinWidth = 430; MinHeight = 380;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        heading.Children.Add(new TextBlock { Text = updateAvailable ? "Coming in Belie " + version : "What's new in Belie " + version,
            FontSize = 26, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var caption = new TextBlock { Text = updateAvailable ? "See what changes before you update." : "What changed, where to find it, and how it works. Available offline.",
            FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush"); heading.Children.Add(caption); root.Children.Add(heading);
        var body = RenderMarkdown(markdown);
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        System.Windows.Automation.AutomationProperties.SetName(scroll, "Release notes");
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var footer = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        var close = new Button { Content = "Close", IsCancel = true, IsDefault = true, MinWidth = 84, Padding = new Thickness(14, 8, 14, 8) };
        close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); footer.Children.Add(close);
        var browser = new Button { Content = "View on GitHub ↗", HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Padding = new Thickness(12, 8, 12, 8) };
        browser.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://github.com/" + Infrastructure.AppUpdater.Repository + "/releases/tag/v" + Uri.EscapeDataString(version)) { UseShellExecute = true }); }
            catch (Exception ex) { browser.ToolTip = "Could not open your browser: " + ex.Message; }
        };
        footer.Children.Add(browser); Grid.SetRow(footer, 2); root.Children.Add(footer);
        UiAppearance.SetDialogContent(this, root, updateAvailable ? "Release notes" : "What's new");
    }

    internal static StackPanel RenderMarkdown(string markdown)
    {
        var body = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("# ", StringComparison.Ordinal)) continue;
            var heading = line.StartsWith("## ", StringComparison.Ordinal);
            var subheading = line.StartsWith("### ", StringComparison.Ordinal);
            var bullet = line.StartsWith("- ", StringComparison.Ordinal);
            if (heading) line = line[3..];
            else if (subheading) line = line[4..];
            else if (bullet) line = "•  " + line[2..];
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = heading ? 21 : subheading ? 16 : 14,
                FontWeight = heading || subheading ? FontWeights.SemiBold : FontWeights.Normal,
                LineHeight = heading ? 29 : subheading ? 24 : 22,
                Margin = new Thickness(bullet ? 8 : 0, heading ? 22 : subheading ? 16 : 0, 0, heading || subheading ? 10 : 9) };
            var parts = line.Split("**", StringSplitOptions.None);
            for (var i = 0; i < parts.Length; i++)
                text.Inlines.Add(new Run(parts[i]) { FontWeight = i % 2 == 1 ? FontWeights.SemiBold : text.FontWeight });
            body.Children.Add(text);
        }
        return body;
    }
}
