using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PrintVault.Core;
using PrintVault.Infrastructure;

namespace PrintVault;

public sealed class ModelIntelligenceWindow : Window
{
    private readonly IReadOnlyList<ModelRecord> models;
    private readonly ModelIntelligenceService intelligence = new();
    private readonly TextBox search = new();
    private readonly ListBox modelList = new();
    private readonly TextBlock summary = new();
    private readonly TextBlock profile = new();
    private readonly ListBox related = new();
    private readonly TextBlock relationshipSummary = new();
    private readonly ModelRecord? initial;

    public ModelIntelligenceWindow(IReadOnlyList<ModelRecord> models, ModelRecord? selected = null)
    {
        this.models = models.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        initial = selected;
        Title = "PrintVault — Model Intelligence";
        Width = 1120;
        Height = 760;
        MinWidth = 900;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(9, 17, 31));
        Foreground = Brushes.White;

        var root = new DockPanel { Margin = new Thickness(18) };
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        heading.Children.Add(new TextBlock { Text = "MODEL INTELLIGENCE", FontSize = 25, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(66, 165, 255)) });
        heading.Children.Add(new TextBlock { Text = "Unified identity, classification, evidence and relationships — derived read-only from the existing library intelligence.", Foreground = new SolidColorBrush(Color.FromRgb(143, 162, 186)), FontSize = 12, Margin = new Thickness(0, 3, 0, 0) });
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var refresh = Button("Refresh", (_, _) => LoadModels());
        var close = Button("Close", (_, _) => Close());
        footer.Children.Add(refresh); footer.Children.Add(close);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(columns, 0);

        var left = new DockPanel { Margin = new Thickness(0, 0, 14, 0) };
        search.Height = 38; search.Padding = new Thickness(10, 4, 10, 4); search.VerticalContentAlignment = VerticalAlignment.Center;
        search.TextChanged += (_, _) => LoadModels();
        DockPanel.SetDock(search, Dock.Top); left.Children.Add(search);
        summary.Margin = new Thickness(2, 8, 0, 8); summary.Foreground = new SolidColorBrush(Color.FromRgb(143, 162, 186)); summary.FontSize = 11;
        DockPanel.SetDock(summary, Dock.Top); left.Children.Add(summary);
        modelList.DisplayMemberPath = nameof(ModelRecord.Name);
        modelList.Background = new SolidColorBrush(Color.FromRgb(16, 27, 45)); modelList.BorderThickness = new Thickness(0); modelList.SelectionChanged += ModelList_SelectionChanged;
        left.Children.Add(modelList);
        Grid.SetColumn(left, 0); columns.Children.Add(left);

        var right = new Grid { Margin = new Thickness(4, 0, 0, 0) };
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(300) });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var profileBorder = Panel();
        var profileStack = new DockPanel { Margin = new Thickness(16) };
        profile.TextWrapping = TextWrapping.Wrap; profile.Foreground = Brushes.White; profile.FontSize = 13; profile.VerticalAlignment = VerticalAlignment.Top;
        profileStack.Children.Add(profile);
        profileBorder.Child = profileStack;
        Grid.SetRow(profileBorder, 0); right.Children.Add(profileBorder);

        var relatedBorder = Panel();
        var relStack = new DockPanel { Margin = new Thickness(16) };
        var relHead = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        relHead.Children.Add(new TextBlock { Text = "RELATIONSHIPS", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(66, 165, 255)) });
        relationshipSummary.Foreground = new SolidColorBrush(Color.FromRgb(143, 162, 186)); relationshipSummary.FontSize = 11; relHead.Children.Add(relationshipSummary);
        DockPanel.SetDock(relHead, Dock.Top); relStack.Children.Add(relHead);
        related.Background = new SolidColorBrush(Color.FromRgb(16, 27, 45)); related.BorderThickness = new Thickness(0);
        relStack.Children.Add(related); relatedBorder.Child = relStack;
        Grid.SetRow(relatedBorder, 1); right.Children.Add(relatedBorder);

        Grid.SetColumn(right, 1); columns.Children.Add(right);
        root.Children.Add(columns);
        Content = root;
        Loaded += (_, _) => LoadModels();
    }

    private void LoadModels()
    {
        var query = search.Text?.Trim() ?? "";
        var filtered = string.IsNullOrWhiteSpace(query)
            ? models
            : models.Where(x => (x.Name + " " + x.TranslatedTitle + " " + x.Category + " " + x.Tags).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        var selected = modelList.SelectedItem as ModelRecord;
        modelList.ItemsSource = filtered;
        summary.Text = $"{filtered.Count:N0} shown • {models.Count:N0} catalog models • intelligence is read-only";

        if (selected is not null && filtered.Contains(selected)) modelList.SelectedItem = selected;
        else if (initial is not null && filtered.Contains(initial)) modelList.SelectedItem = initial;
        else if (filtered.Count > 0) modelList.SelectedIndex = 0;
    }

    private void ModelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (modelList.SelectedItem is not ModelRecord model) return;
        var p = intelligence.BuildProfile(model, models);
        profile.Text =
            $"{p.Name}\n" +
            (string.IsNullOrWhiteSpace(p.TranslatedTitle) ? "" : $"{p.TranslatedTitle}\n") +
            $"\nIdentity: {(string.IsNullOrWhiteSpace(p.EntityName) ? "No named entity" : $"{p.EntityName} • {p.EntityDomain}")} ({p.IdentityConfidence}%)\n" +
            $"Category: {p.Category}{(model.CategoryOverride ? " [PROTECTED]" : "")}\n" +
            $"Classification: {p.SemanticType} / {p.Subtype}\n" +
            $"Family: {p.Family}\n" +
            $"Classification confidence: {p.ClassificationConfidence}%\n" +
            $"Possible duplicate: {(p.PossibleDuplicate ? "Yes" : "No")}\n" +
            $"Relationship candidates: {p.RelatedCount:N0}\n" +
            $"Top relationships shown: {p.DisplayedRelationshipCount:N0}\n\n" +
            $"TAGS\n{(p.IntelligenceTags.Length == 0 ? "None" : string.Join(" • ", p.IntelligenceTags))}\n\n" +
            $"EVIDENCE\n{(string.IsNullOrWhiteSpace(p.Evidence) ? "No detailed evidence recorded." : p.Evidence)}";

        var rel = intelligence.FindRelated(model, models, 20);
        related.Items.Clear();
        foreach (var item in rel)
        {
            var title = string.IsNullOrWhiteSpace(item.Model.TranslatedTitle) ? item.Model.Name : item.Model.TranslatedTitle;
            related.Items.Add(new ListBoxItem
            {
                Content = $"{item.Score,3}%  {item.Relationship}\n      {item.Model.Name}\n      {item.Model.Category} • {title}\n      {item.Evidence}",
                Tag = item.Model,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 4)
            });
        }
        relationshipSummary.Text = rel.Count == 0 ? "No strong relationships detected." : $"Showing top {rel.Count} of {p.RelatedCount:N0} relationship candidates, ranked by shared identity, duplicate hash, semantic domain and metadata overlap.";
    }

    private static Border Panel() => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(16, 27, 45)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(41, 65, 95)),
        BorderThickness = new Thickness(1),
        Margin = new Thickness(0, 0, 0, 10)
    };

    private static Button Button(string text, RoutedEventHandler handler)
    {
        var b = new Button { Content = text, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(5, 4, 0, 4), Background = new SolidColorBrush(Color.FromRgb(22, 37, 59)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        b.Click += handler;
        return b;
    }
}
