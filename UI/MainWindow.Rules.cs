using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Control = System.Windows.Controls.Control;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.UI;

/// <summary>Schedules and triggers, edited right on the profile's Automation tab.</summary>
internal partial class MainWindow
{
    private readonly ObservableCollection<ScheduleRuleVm> _rules = new();
    private readonly ObservableCollection<TriggerVm> _triggers = new();

    private void SetUpRuleEditing()
    {
        RulesList.ItemsSource = _rules;
        TriggersList.ItemsSource = _triggers;
        _rules.CollectionChanged += (_, _) => RulesChanged();
        _triggers.CollectionChanged += (_, _) => RulesChanged();
        // Day boxes and time pickers write straight into the rows; look again once they have.
        void Later(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(UpdateDraftState, DispatcherPriority.Input);
        foreach (var list in new[] { RulesList, TriggersList })
        {
            list.AddHandler(ToggleButton.CheckedEvent, (RoutedEventHandler)Later);
            list.AddHandler(ToggleButton.UncheckedEvent, (RoutedEventHandler)Later);
            list.AddHandler(Selector.SelectionChangedEvent, (SelectionChangedEventHandler)((s, e) => Later(s, e)));
            list.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, (TextChangedEventHandler)((s, e) => Later(s, e)));
        }
    }

    private void LoadRules(WallpaperProfile? profile)
    {
        _rules.Clear(); _triggers.Clear();
        if (profile == null) return;
        foreach (var rule in profile.Schedule) _rules.Add(new ScheduleRuleVm(rule));
        foreach (var trigger in profile.EventTriggers) _triggers.Add(new TriggerVm(trigger));
    }

    private void RulesChanged()
    {
        NoRulesHint.Visibility = _rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoTriggersHint.Visibility = _triggers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateDraftState();
    }

    private object RulesSnapshot() => new
    {
        Rules = _rules.Select(row => string.Join(",", row.Days) + "|" + row.StartTimeText + "|" + row.EndTimeText).ToArray(),
        Triggers = _triggers.Select(row => row.Type + "|" + row.Condition + "|" + row.PriorityText).ToArray()
    };

    private bool TryCollectRules(out List<ScheduleRule> rules, out List<EventTrigger> triggers)
    {
        if (RuleEditing.TryBuild(_rules, _triggers, out rules, out triggers, out var error)) return true;
        var list = error.Contains("trigger", StringComparison.OrdinalIgnoreCase) ? TriggersList : RulesList;
        ProfileTabs.SelectedIndex = 2; ProfileTabs.UpdateLayout();
        return InvalidField(error, FirstInput(list) ?? (Control)AddRuleButton, 2);
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        _rules.Add(new ScheduleRuleVm(new ScheduleRule
        {
            StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0),
            DaysOfWeek = new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
        }));
    }

    private void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ScheduleRuleVm row) _rules.Remove(row);
    }

    private void AddTrigger_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        _triggers.Add(new TriggerVm(new EventTrigger { Priority = 5 }));
    }

    private void RemoveTrigger_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TriggerVm row) _triggers.Remove(row);
    }

    private static Control? FirstInput(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is TextBox or ComboBox or CheckBox && child is Control { IsVisible: true, IsEnabled: true, Focusable: true } input) return input;
            if (FirstInput(child) is { } nested) return nested;
        }
        return null;
    }
}
