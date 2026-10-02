using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PrintVault.Core;
using PrintVault.Infrastructure;

namespace PrintVault;

public sealed class SemanticRelationshipIntegrationChallengeWindow : Window
{
    private readonly SemanticRelationshipIntegrationChallengeService service;
    private readonly TextBlock summary = new();
    private readonly ListView list = new();
    private Button? runButton;
    private SemanticRelationshipIntegrationChallengeResult? result;

    public SemanticRelationshipIntegrationChallengeWindow(LibraryRepository repository)
    {
        service = new SemanticRelationshipIntegrationChallengeService(repository);
        Title = "Semantic + Relationship Integration Challenge";
        Width = 1700; Height = 920; MinWidth = 1250; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)Application.Current.Resources["Panel"];
        Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Text"];

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = new TextBlock { Text = "SEMANTIC + RELATIONSHIP INTEGRATION CHALLENGE", FontSize = 22, FontWeight = FontWeights.Bold };
        Grid.SetRow(title, 0); root.Children.Add(title);
        summary.Margin = new Thickness(0, 8, 0, 12); summary.TextWrapping = TextWrapping.Wrap;
        summary.Text = "Integration gate: semantic entity identity is evaluated independently first, then the relationship engine is checked against hard-coded expectations. HueForge size, printer context and generic vocabulary cannot establish identity.";
        Grid.SetRow(summary, 1); root.Children.Add(summary);

        var view = new GridView();
        foreach (var c in new (string Header, string Property, double Width)[]
        {
            ("Result", "Result", 65), ("Case", "CaseType", 250), ("File A", "ModelA", 245), ("File B", "ModelB", 245),
            ("Expected Entities", "ExpectedEntitySummary", 180), ("Actual Entities", "EntitySummary", 180),
            ("Expected Rel.", "ExpectedRelationship", 125), ("Actual", "ActualRelationship", 125), ("Score", "Score", 65), ("Basis / Evidence", "BasisAndEvidence", 390)
        })
            view.Columns.Add(new GridViewColumn { Header = c.Header, DisplayMemberBinding = new System.Windows.Data.Binding(c.Property), Width = c.Width });
        // Add a computed display property for expected entities without changing the record contract.
        view.Columns[4].DisplayMemberBinding = new System.Windows.Data.Binding { Path = new PropertyPath("ExpectedEntityA") };
        list.View = view; list.Background = (System.Windows.Media.Brush)Application.Current.Resources["Panel2"];
        Grid.SetRow(list, 2); root.Children.Add(list);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var run = new Button { Content = "Run Integration Test", Width = 150, Height = 36, Margin = new Thickness(0, 0, 8, 0) };
        runButton = run; run.Click += async (_, _) => await RunAsync();
        var report = new Button { Content = "Open Report", Width = 120, Height = 36, Margin = new Thickness(0, 0, 8, 0) }; report.Click += (_, _) => OpenReport();
        var close = new Button { Content = "Close", Width = 100, Height = 36 }; close.Click += (_, _) => Close();
        buttons.Children.Add(run); buttons.Children.Add(report); buttons.Children.Add(close);
        Grid.SetRow(buttons, 3); root.Children.Add(buttons);
        Content = root;
        Loaded += async (_, _) => await RunAsync();
    }

    private async System.Threading.Tasks.Task RunAsync()
    {
        if (runButton is not null) runButton.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            summary.Text = "RUNNING — evaluating semantic entity identity first, then relationship behavior on a temporary SQLite snapshot. Production data is read-only.";
            result = await System.Threading.Tasks.Task.Run(() => service.Run());
            list.ItemsSource = result.Rows;
            summary.Text = $"Catalog: {result.CatalogSize:N0} • Cases: {result.Cases} • Passed: {result.Passed} • Failed: {result.Failed} • " +
                           $"Real available: {result.AvailableRealCases} • Real unavailable: {result.UnavailableRealCases} • Runtime: {result.Elapsed.TotalSeconds:F1}s • " +
                           (result.Failed == 0 ? "ALL INTEGRATION CASES PASSED." : "INTEGRATION FAILURES DETECTED — open the report.");
        }
        catch (System.Exception ex) { summary.Text = "Semantic + relationship integration challenge failed: " + ex.Message; }
        finally { Mouse.OverrideCursor = null; if (runButton is not null) runButton.IsEnabled = true; }
    }

    private void OpenReport()
    {
        if (result is null) return;
        try { Process.Start(new ProcessStartInfo(result.ReportPath) { UseShellExecute = true }); } catch { }
    }
}
