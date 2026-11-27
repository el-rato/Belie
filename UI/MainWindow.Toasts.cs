using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace WallpaperProfiles.UI;

internal partial class MainWindow
{
    private readonly List<(Border Toast, DateTime Until)> _toasts = new();
    private DispatcherTimer? _toastTimer;

    /// <summary>A short note that slides in at the bottom right and leaves on its own after a few seconds.</summary>
    internal void ShowToast(string text)
    {
        if (!IsLoaded) return;
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentBrush");
        row.Children.Add(dot);
        row.Children.Add(new TextBlock { Text = text, FontSize = 13, MaxWidth = 320, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var toast = new Border { Child = row, CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 9, 16, 9),
            BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            RenderTransform = new TranslateTransform() };
        toast.SetResourceReference(Border.BackgroundProperty, "SurfaceRaisedBrush");
        toast.SetResourceReference(Border.BorderBrushProperty, "PaneEdgeBrush");
        toast.SetResourceReference(EffectProperty, "FloatElevation");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(toast, System.Windows.Automation.AutomationLiveSetting.Polite);
        System.Windows.Automation.AutomationProperties.SetName(toast, text);
        while (ToastHost.Children.Count >= 3) RemoveToast(_toasts[0].Toast);
        ToastHost.Children.Add(toast);
        _toasts.Add((toast, DateTime.UtcNow.AddSeconds(2.8)));
        if (SystemParameters.ClientAreaAnimation)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            toast.RenderTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(36, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { FillBehavior = FillBehavior.Stop });
        }
        if (_toastTimer == null)
        {
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _toastTimer.Tick += (_, _) => ExpireToasts();
            Closed += (_, _) => { _toastTimer.Stop(); _toasts.Clear(); ToastHost.Children.Clear(); };
        }
        _toastTimer.Start();
    }

    private void ExpireToasts()
    {
        foreach (var (toast, until) in _toasts.ToArray())
        {
            if (DateTime.UtcNow < until || toast.Tag is "Leaving") continue;
            toast.Tag = "Leaving";
            if (!SystemParameters.ClientAreaAnimation) { RemoveToast(toast); continue; }
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
            fade.Completed += (_, _) => RemoveToast(toast);
            toast.BeginAnimation(OpacityProperty, fade);
        }
        if (_toasts.Count == 0) _toastTimer?.Stop();
    }

    private void RemoveToast(Border toast)
    {
        toast.BeginAnimation(OpacityProperty, null);
        ToastHost.Children.Remove(toast);
        _toasts.RemoveAll(entry => entry.Toast == toast);
    }
}
