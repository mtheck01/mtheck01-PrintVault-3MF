using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PrintVault.Core;
using PrintVault.Infrastructure;

namespace PrintVault;

public sealed class DuplicateManagerWindow : Window
{
    private readonly LibraryRepository repo;
    private readonly LibraryHealthService health;
    private readonly Action refreshMain;
    private readonly ListBox list = new();
    private readonly TextBlock summary = new();

    public DuplicateManagerWindow(LibraryRepository repo, LibraryHealthService health, Action refreshMain)
    {
        this.repo = repo;
        this.health = health;
        this.refreshMain = refreshMain;
        Title = "PrintVault — Duplicate Manager";
        Width = 1120; Height = 760;
        MinWidth = 900; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(9, 17, 31));
        Foreground = Brushes.White;

        var grid = new Grid { Margin = new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "DUPLICATE MANAGER", FontSize = 25, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(66, 165, 255)) });
        header.Children.Add(new TextBlock { Text = "Exact hash matches only. Nothing is removed automatically.", Foreground = Brushes.LightGray, Margin = new Thickness(0, 4, 0, 10) });
        Grid.SetRow(header, 0); grid.Children.Add(header);

        summary.Foreground = Brushes.LightGray;
        summary.Margin = new Thickness(0, 0, 0, 10);
        Grid.SetRow(summary, 1); grid.Children.Add(summary);

        list.Background = new SolidColorBrush(Color.FromRgb(16, 27, 45));
        list.BorderThickness = new Thickness(0);
        list.SelectionMode = SelectionMode.Single;
        list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Grid.SetRow(list, 2); grid.Children.Add(list);

        var bar = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var open = B("Open Location", Open); var refresh = B("Refresh", Load); var close = B("Close", (_, _) => Close());
        Grid.SetColumn(open, 0); Grid.SetColumn(refresh, 1); Grid.SetColumn(close, 3);
        bar.Children.Add(open); bar.Children.Add(refresh); bar.Children.Add(close);
        Grid.SetRow(bar, 3); grid.Children.Add(bar);

        Content = grid;
        Loaded += (_, _) => Load();
    }

    private Button B(string text, RoutedEventHandler handler)
    {
        var b = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 10, 0),
            Padding = new Thickness(16, 9, 16, 9),
            MinWidth = 110,
            Height = 38,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(22, 37, 59))
        };
        b.Click += handler;
        return b;
    }

    private void Load(object? sender = null, RoutedEventArgs? e = null)
    {
        list.Items.Clear();
        var groups = health.Duplicates().OrderByDescending(x => x.Models.Count).ThenByDescending(x => x.Bytes).ToList();
        var files = groups.Sum(x => x.Models.Count);
        var bytes = groups.Sum(x => x.Bytes);
        summary.Text = groups.Count == 0
            ? "No exact duplicate groups found."
            : $"{groups.Count:N0} duplicate groups • {files:N0} files • {bytes / 1024d / 1024d:0.0} MB involved";

        foreach (var g in groups)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(16, 27, 45)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(31, 49, 75)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = $"{g.Models.Count:N0} files  •  {g.Bytes / 1024d / 1024d:0.0} MB  •  HASH {g.Hash[..Math.Min(12, g.Hash.Length)]}",
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(66, 165, 255))
            });
            foreach (var model in g.Models.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var row = new Grid { Margin = new Thickness(0, 7, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var name = new TextBlock { Text = model.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
                var path = new TextBlock { Text = model.Path, Foreground = Brushes.LightGray, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
                var details = new StackPanel(); details.Children.Add(name); details.Children.Add(path);
                Grid.SetColumn(details, 0); row.Children.Add(details);
                var open = new Button { Content = "Open", Width = 72, Height = 30, Margin = new Thickness(12, 0, 0, 0), Tag = model };
                open.Click += (_, _) => OpenModel(model);
                Grid.SetColumn(open, 1); row.Children.Add(open);
                panel.Children.Add(row);
            }
            card.Child = panel;
            list.Items.Add(new ListBoxItem { Content = card, Tag = g.Models.FirstOrDefault(), Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent });
        }
    }

    private void Open(object? sender, RoutedEventArgs e)
    {
        if (list.SelectedItem is ListBoxItem item && item.Tag is ModelRecord model) OpenModel(model);
    }

    private static void OpenModel(ModelRecord model)
    {
        if (!File.Exists(model.Path)) return;
        ProcessStart(model.Path);
    }

    private static void ProcessStart(string path)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }
}
