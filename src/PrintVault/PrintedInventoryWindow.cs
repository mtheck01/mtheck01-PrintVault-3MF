using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using PrintVault.Core;
using PrintVault.Infrastructure;

namespace PrintVault;

/// <summary>At-a-glance on-hand inventory for physical prints; independent of file organization.</summary>
public sealed class PrintedInventoryWindow : Window
{
    private readonly PrintedStockService service;
    private readonly IReadOnlyList<ModelRecord> models;
    private readonly DataGrid grid = new();
    private readonly TextBox search = new();
    private readonly CheckBox showArchived = new() { Content = "Show archived", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock summary = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.LightGray };
    private List<PrintedStockItem> items = [];

    public PrintedInventoryWindow(PrintedStockService service, IReadOnlyList<ModelRecord> models)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.models = models ?? [];
        Title = "PrintVault — Printed Stock";
        Width = 1120; Height = 700; MinWidth = 850; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#09111F"); Foreground = Brushes.White;
        Content = BuildLayout();
        showArchived.Checked += (_, _) => LoadItems();
        showArchived.Unchecked += (_, _) => LoadItems();
        search.TextChanged += (_, _) => ApplyFilter();
        Loaded += (_, _) => LoadItems();
    }

    private UIElement BuildLayout()
    {
        var root = new DockPanel { Margin = new Thickness(22) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "PRINTED STOCK", FontSize = 25, FontWeight = FontWeights.Bold, Foreground = Brush("#42A5FF") });
        header.Children.Add(new TextBlock { Text = "Track physical prints on hand. Stock counts are independent from your 3MF files and library scans.", Margin = new Thickness(0, 4, 0, 12), Foreground = Brushes.LightGray });
        var controls = new DockPanel(); header.Children.Add(controls);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(actions, Dock.Right); controls.Children.Add(actions);
        actions.Children.Add(Button("Add stock item", AddItem));
        actions.Children.Add(Button("Edit details", EditItem));
        actions.Children.Add(Button("Adjust quantity", AdjustItem));
        actions.Children.Add(Button("History", ShowHistory));
        actions.Children.Add(Button("Archive", ArchiveItem));
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(left);
        search.Width = 270; search.Height = 34; search.Margin = new Thickness(0, 0, 12, 0);
        search.Padding = new Thickness(8, 5, 8, 5); search.ToolTip = "Search model, variant, location and notes";
        search.Background = Brush("#101B2D"); search.Foreground = Brush("#F2F6FC"); search.BorderBrush = Brush("#405875"); search.CaretBrush = Brushes.White;
        search.SelectionBrush = Brush("#316A9E"); search.VerticalContentAlignment = VerticalAlignment.Center;
        showArchived.Foreground = Brush("#E5EDF7"); showArchived.FontSize = 13; showArchived.Margin = new Thickness(0, 0, 4, 0);
        left.Children.Add(search); left.Children.Add(showArchived);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        summary.Margin = new Thickness(2, 0, 0, 0); footer.Children.Add(summary);
        var close = Button("Close", (_, _) => Close()); close.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(close, 1); footer.Children.Add(close);

        grid.AutoGenerateColumns = false; grid.IsReadOnly = true; grid.SelectionMode = DataGridSelectionMode.Single;
        grid.SelectionUnit = DataGridSelectionUnit.FullRow; grid.CanUserAddRows = false; grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal; grid.Background = Brush("#101B2D"); grid.Foreground = Brushes.White;
        grid.RowBackground = Brush("#101B2D"); grid.AlternatingRowBackground = Brush("#142239");
        grid.Columns.Add(Column("Model", nameof(PrintedStockItem.ModelName), 2));
        grid.Columns.Add(Column("Variant", nameof(PrintedStockItem.Variant), 1));
        grid.Columns.Add(Column("Location", nameof(PrintedStockItem.Location), 1));
        grid.Columns.Add(Column("Condition", nameof(PrintedStockItem.Condition), 1));
        grid.Columns.Add(Column("On hand", nameof(PrintedStockItem.Quantity), .65));
        grid.Columns.Add(Column("Low at", nameof(PrintedStockItem.LowStockThreshold), .65));
        grid.Columns.Add(Column("Stock status", nameof(PrintedStockItem.StockStatus), .85));
        grid.Columns.Add(Column("Notes", nameof(PrintedStockItem.Notes), 1.5));
        ApplyDarkGridStyles(grid);
        root.Children.Add(grid);
        return root;
    }

    private void LoadItems()
    {
        try
        {
            items = service.GetItems(showArchived.IsChecked == true).ToList();
            ApplyFilter();
        }
        catch (Exception ex) { Error("Could not load printed stock", ex); }
    }

    private void ApplyFilter()
    {
        var term = search.Text.Trim();
        var visible = items.Where(x => string.IsNullOrEmpty(term) ||
            $"{x.ModelName} {x.Variant} {x.Location} {x.Condition} {x.Notes}".Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        grid.ItemsSource = visible;
        var active = items.Where(x => !x.IsArchived).ToList();
        summary.Text = $"{active.Count:N0} items  •  {active.Sum(x => (long)x.Quantity):N0} units on hand  •  {active.Count(x => x.IsLowStock):N0} low stock";
    }

    private PrintedStockItem? Selected => grid.SelectedItem as PrintedStockItem;

    private void AddItem(object? sender, RoutedEventArgs e)
    {
        var dialog = new StockItemDialog(models) { Owner = this, Title = "Add printed stock item" };
        if (dialog.ShowDialog() != true) return;
        try { service.Add(dialog.LinkedModelId, dialog.ModelName, dialog.Variant, dialog.Location, dialog.Condition, dialog.Quantity, dialog.LowStockThreshold, dialog.Notes); LoadItems(); }
        catch (Exception ex) { Error("Could not add stock item", ex); }
    }

    private void EditItem(object? sender, RoutedEventArgs e)
    {
        var item = Selected; if (item is null || item.IsArchived) { MessageBox.Show(this, "Select an active stock item first.", "Edit stock item", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var dialog = new StockItemDialog(models, item) { Owner = this, Title = "Edit printed stock details" };
        if (dialog.ShowDialog() != true) return;
        try { service.UpdateDetails(item with { LinkedModelId = dialog.LinkedModelId, ModelName = dialog.ModelName, Variant = dialog.Variant, Location = dialog.Location, Condition = dialog.Condition, LowStockThreshold = dialog.LowStockThreshold, Notes = dialog.Notes }); LoadItems(); }
        catch (Exception ex) { Error("Could not update stock item", ex); }
    }

    private void AdjustItem(object? sender, RoutedEventArgs e)
    {
        var item = Selected; if (item is null || item.IsArchived) { MessageBox.Show(this, "Select an active stock item first.", "Adjust quantity", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var dialog = new StockAdjustmentDialog(item) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try { service.Adjust(item.Id, dialog.Delta, dialog.Reason); LoadItems(); }
        catch (Exception ex) { Error("Could not adjust stock quantity", ex); }
    }

    private void ShowHistory(object? sender, RoutedEventArgs e)
    {
        var item = Selected; if (item is null) { MessageBox.Show(this, "Select a stock item to view its history.", "Stock history", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        new StockHistoryWindow(item, service.GetMovements(item.Id)) { Owner = this }.ShowDialog();
    }

    private void ArchiveItem(object? sender, RoutedEventArgs e)
    {
        var item = Selected; if (item is null || item.IsArchived) { MessageBox.Show(this, "Select an active stock item first.", "Archive stock item", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (MessageBox.Show(this, $"Archive '{item.ModelName}'? Its quantity and history will be retained.", "Archive stock item", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { service.Archive(item.Id); LoadItems(); }
        catch (Exception ex) { Error("Could not archive stock item", ex); }
    }

    private void Error(string title, Exception ex) => MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    internal static void ApplyDarkGridStyles(DataGrid target)
    {
        target.ColumnHeaderStyle = new Style(typeof(DataGridColumnHeader))
        {
            Setters =
            {
                new Setter(Control.BackgroundProperty, Brush("#1A2C44")),
                new Setter(Control.ForegroundProperty, Brush("#F2F6FC")),
                new Setter(Control.BorderBrushProperty, Brush("#405875")),
                new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)),
                new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)),
                new Setter(Control.FontWeightProperty, FontWeights.SemiBold)
            }
        };
        target.CellStyle = new Style(typeof(DataGridCell))
        {
            Setters =
            {
                new Setter(Control.BackgroundProperty, Brush("#101B2D")),
                new Setter(Control.ForegroundProperty, Brush("#F2F6FC")),
                new Setter(Control.PaddingProperty, new Thickness(7, 4, 7, 4)),
                new Setter(Control.BorderBrushProperty, Brush("#263B55")),
                new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1))
            },
            Triggers =
            {
                new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true, Setters = { new Setter(Control.BackgroundProperty, Brush("#285783")), new Setter(Control.ForegroundProperty, Brushes.White) } }
            }
        };
    }
    private static DataGridTextColumn Column(string header, string property, double weight)
        => new() { Header = header, Binding = new Binding(property), Width = new DataGridLength(weight, DataGridLengthUnitType.Star) };
    private static Button Button(string title, RoutedEventHandler click)
    {
        var button = new Button { Content = title, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(11, 8, 11, 8), Foreground = Brushes.White, Background = Brush("#16253B") };
        button.Click += click; return button;
    }
}

internal sealed class StockItemDialog : Window
{
    private readonly ComboBox modelPicker = new() { DisplayMemberPath = nameof(ModelRecord.Name), MinWidth = 290, IsEditable = true };
    private readonly TextBox name = new(), variant = new(), location = new(), threshold = new(), quantity = new(), notes = new();
    private readonly ComboBox condition = new() { ItemsSource = PrintedStockService.Conditions, MinWidth = 180 };
    private bool editing;

    public long? LinkedModelId => (modelPicker.SelectedItem as ModelRecord)?.Id;
    public string ModelName => (modelPicker.SelectedItem as ModelRecord)?.Name.Trim() is { Length: > 0 } linked ? linked : name.Text.Trim();
    public string Variant => variant.Text;
    public string Location => location.Text;
    public string Condition => condition.SelectedItem?.ToString() ?? "Good";
    public int Quantity => Parse(quantity, "On-hand quantity");
    public int LowStockThreshold => Parse(threshold, "Low-stock threshold");
    public string Notes => notes.Text;

    public StockItemDialog(IReadOnlyList<ModelRecord> models, PrintedStockItem? item = null)
    {
        editing = item is not null;
        Width = 640; Height = editing ? 570 : 620; MinWidth = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(9, 17, 31)); Foreground = Brushes.White;
        modelPicker.IsEditable = false;
        modelPicker.ItemsSource = models.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        condition.SelectedItem = "Good";
        var root = new DockPanel { Margin = new Thickness(22) };
        var title = new TextBlock { Text = item is null ? "ADD PRINTED STOCK" : "EDIT ITEM DETAILS", FontSize = 21, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(66, 165, 255)), Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(5) };
        var save = new Button { Content = item is null ? "Add item" : "Save details", IsDefault = true, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(5) };
        save.Click += Save_Click; buttons.Children.Add(cancel); buttons.Children.Add(save);
        var fields = new StackPanel(); root.Children.Add(fields);
        AddField(fields, "Link to a library model (optional)", modelPicker);
        var clearLink = new Button { Content = "Clear model link (use manual name)", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 9), Padding = new Thickness(8, 4, 8, 4) };
        clearLink.Click += (_, _) => modelPicker.SelectedItem = null;
        fields.Children.Add(clearLink);
        AddField(fields, "Model name (for unlinked or custom items)", name);
        AddField(fields, "Variant / size / color", variant);
        AddField(fields, "Storage location", location);
        AddField(fields, "Condition", condition);
        AddField(fields, "On hand", quantity);
        AddField(fields, "Low-stock alert at (0 disables)", threshold);
        AddField(fields, "Notes", notes);
        name.ToolTip = "A linked library model name is used automatically; enter a name for custom stock.";
        modelPicker.SelectionChanged += (_, _) => { if (modelPicker.SelectedItem is ModelRecord m) name.Text = m.Name; };
        if (item is not null)
        {
            var linked = models.FirstOrDefault(x => x.Id == item.LinkedModelId);
            modelPicker.SelectedItem = linked; name.Text = linked is null ? item.ModelName : linked.Name;
            variant.Text = item.Variant; location.Text = item.Location; condition.SelectedItem = item.Condition;
            quantity.Text = item.Quantity.ToString(); quantity.IsEnabled = false; quantity.ToolTip = "Use Adjust quantity to preserve a complete change history.";
            threshold.Text = item.LowStockThreshold.ToString(); notes.Text = item.Notes;
        }
        else { quantity.Text = "0"; threshold.Text = "0"; }
        Content = root;
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ModelName)) throw new ArgumentException("Choose a library model or enter a model name.");
            _ = Quantity; _ = LowStockThreshold;
            if (condition.SelectedItem is null) throw new ArgumentException("Choose an item condition.");
            DialogResult = true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Check stock details", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private static int Parse(TextBox field, string label)
        => int.TryParse(field.Text, out var value) && value >= 0 ? value : throw new ArgumentException($"{label} must be a whole number of zero or greater.");

    internal static void AddField(Panel parent, string label, Control field)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(235) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.LightGray, Margin = new Thickness(0, 0, 12, 0) });
        Grid.SetColumn(field, 1); field.Height = 31; row.Children.Add(field); parent.Children.Add(row);
    }
}

internal sealed class StockAdjustmentDialog : Window
{
    private readonly TextBox delta = new(), reason = new();
    public int Delta => int.Parse(delta.Text);
    public string Reason => reason.Text.Trim();

    public StockAdjustmentDialog(PrintedStockItem item)
    {
        Title = "Adjust printed stock"; Width = 470; Height = 280; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = new SolidColorBrush(Color.FromRgb(9, 17, 31)); Foreground = Brushes.White;
        delta.Text = "1";
        var root = new DockPanel { Margin = new Thickness(20) };
        var info = new TextBlock { Text = $"{item.ModelName} — currently {item.Quantity} on hand\nEnter a signed change (for example +2 or -1).", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 15) };
        DockPanel.SetDock(info, Dock.Top); root.Children.Add(info);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(5), Padding = new Thickness(12, 7, 12, 7) };
        var save = new Button { Content = "Record adjustment", IsDefault = true, Margin = new Thickness(5), Padding = new Thickness(12, 7, 12, 7) };
        save.Click += (_, _) =>
        {
            if (!int.TryParse(delta.Text, out var parsed) || parsed == 0) { MessageBox.Show(this, "Enter a non-zero whole number, such as +2 or -1.", "Invalid adjustment", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (string.IsNullOrWhiteSpace(reason.Text)) { MessageBox.Show(this, "Enter a reason for the stock change.", "Reason required", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (parsed < -item.Quantity) { MessageBox.Show(this, $"This item has {item.Quantity} on hand; the adjustment cannot make it negative.", "Insufficient stock", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            DialogResult = true;
        };
        actions.Children.Add(cancel); actions.Children.Add(save);
        var fields = new StackPanel(); root.Children.Add(fields);
        StockItemDialog.AddField(fields, "Quantity change", delta); StockItemDialog.AddField(fields, "Reason", reason);
        Content = root;
    }
}

internal sealed class StockHistoryWindow : Window
{
    public StockHistoryWindow(PrintedStockItem item, IReadOnlyList<StockMovement> movements)
    {
        Title = $"PrintVault — Stock history: {item.ModelName}"; Width = 820; Height = 480; MinWidth = 650; MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = new SolidColorBrush(Color.FromRgb(9, 17, 31)); Foreground = Brushes.White;
        var root = new DockPanel { Margin = new Thickness(20) };
        var heading = new TextBlock { Text = $"QUANTITY HISTORY — {item.ModelName}", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(66, 165, 255)), Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 12, 0, 0) };
        close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close);
        var grid = new DataGrid { ItemsSource = movements, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, Background = new SolidColorBrush(Color.FromRgb(16, 27, 45)), Foreground = Brushes.White };
        grid.Columns.Add(new DataGridTextColumn { Header = "When (UTC)", Binding = new Binding(nameof(StockMovement.OccurredUtc)) { StringFormat = "yyyy-MM-dd HH:mm:ss" }, Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Change", Binding = new Binding(nameof(StockMovement.QuantityDelta)) { StringFormat = "+#;-#;0" }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "On hand after", Binding = new Binding(nameof(StockMovement.QuantityAfter)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Reason", Binding = new Binding(nameof(StockMovement.Reason)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        PrintedInventoryWindow.ApplyDarkGridStyles(grid);
        root.Children.Add(grid); Content = root;
    }
}
