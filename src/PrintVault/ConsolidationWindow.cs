using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PrintVault.Infrastructure;

namespace PrintVault;

public sealed class ConsolidationWindow : Window
{
    private readonly ConsolidationPlan plan;
    private readonly ListBox list;
    private readonly CheckBox includeReview;
    public bool Applied { get; private set; }

    public ConsolidationWindow(ConsolidationPlan plan, Window owner)
    {
        Owner = owner;
        Title = "PrintVault • Smart Library Consolidation";
        Width = 980; Height = 700; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(13,23,39));
        Foreground = Brushes.White;
        this.plan = plan;

        var root = new DockPanel { Margin = new Thickness(20) };
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "Smart Library Consolidation", FontSize = 25, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = $"PrintVault analyzed the existing folder structure and built a file-level migration plan. {plan.Moves.Count:N0} files can be consolidated; {plan.ReviewCount:N0} are marked for review; {plan.IgnoredCount:N0} are left untouched.", Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,14) });
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);

        list = new ListBox { Background = new SolidColorBrush(Color.FromRgb(24,37,57)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(70,90,115)), SelectionMode = SelectionMode.Multiple };
        foreach (var x in plan.Groups)
        {
            var review = string.Equals(x.Confidence, "Review", StringComparison.OrdinalIgnoreCase);
            var item = new ListBoxItem
            {
                Content = $"{x.SourceFolder}  →  {x.TargetCategory}    ({x.FileCount:N0} 3MF, {x.Bytes/1024d/1024d:0.0} MB)    [{x.Confidence}]\n{x.Reason}",
                Tag = x,
                IsSelected = !review,
                Padding = new Thickness(10)
            };
            list.Items.Add(item);
        }
        root.Children.Add(list);

        includeReview = new CheckBox { Content = "Include Review items (selects review groups for migration)", IsChecked = false, Foreground = Brushes.White, Margin = new Thickness(0,12,0,6) };
        includeReview.Checked += (_, _) => SetReviewSelection(true);
        includeReview.Unchecked += (_, _) => SetReviewSelection(false);
        DockPanel.SetDock(includeReview, Dock.Bottom); root.Children.Add(includeReview);

        var footer = new StackPanel();
        footer.Children.Add(new TextBlock { Text = "Nothing is deleted automatically. Duplicate filenames receive a safe numbered suffix. An undo manifest is saved so the last consolidation can be reversed.", Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,10) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Width = 100, Margin = new Thickness(0,0,8,0) };
        cancel.Click += (_,_) => Close();
        var apply = new Button { Content = "Apply Consolidation", Width = 165 };
        apply.Click += (_,_) => { Applied = true; DialogResult = true; Close(); };
        buttons.Children.Add(cancel); buttons.Children.Add(apply); footer.Children.Add(buttons);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        Content = root;
    }

    public bool IncludeReview => includeReview.IsChecked == true;
    public IReadOnlyList<ConsolidationGroup> SelectedGroups() => list.SelectedItems.OfType<ListBoxItem>().Select(x => (ConsolidationGroup)x.Tag).ToList();

    private void SetReviewSelection(bool selected)
    {
        foreach (var item in list.Items.OfType<ListBoxItem>())
        {
            if (item.Tag is ConsolidationGroup group && string.Equals(group.Confidence, "Review", StringComparison.OrdinalIgnoreCase))
                item.IsSelected = selected;
        }
    }
}
