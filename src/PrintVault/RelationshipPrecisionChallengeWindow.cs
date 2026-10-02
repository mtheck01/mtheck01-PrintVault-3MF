using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PrintVault.Core;
using PrintVault.Infrastructure;

namespace PrintVault;

public sealed class RelationshipPrecisionChallengeWindow : Window
{
    private readonly RelationshipPrecisionChallengeService service;
    private readonly TextBlock summary = new();
    private readonly ListView list = new();
    private Button? runButton;
    private RelationshipPrecisionChallengeResult? result;

    public RelationshipPrecisionChallengeWindow(LibraryRepository repository)
    {
        service = new RelationshipPrecisionChallengeService(repository);
        Title = "Relationship Identity Gate Challenge";
        Width = 1650; Height = 900; MinWidth = 1250; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)Application.Current.Resources["Panel"];
        Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Text"];

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock { Text = "RELATIONSHIP IDENTITY GATE & PRECISION CHALLENGE", FontSize = 22, FontWeight = FontWeights.Bold };
        Grid.SetRow(title, 0); root.Children.Add(title);
        summary.Margin = new Thickness(0, 8, 0, 12); summary.TextWrapping = TextWrapping.Wrap;
        summary.Text = "Full-library relationship audit. Context such as HueForge size, printer/device, orientation and generic vocabulary cannot establish identity. The test uses independent semantic expectations and a selective relationship index.";
        Grid.SetRow(summary, 1); root.Children.Add(summary);

        var view = new GridView();
        foreach (var c in new (string Header, string Property, double Width)[]
        {
            ("Result", "Result", 65), ("Test Type", "CaseType", 250), ("File A", "ModelA", 275), ("File B", "ModelB", 275),
            ("Expected", "Expected", 130), ("Actual", "Actual", 150), ("Score", "Score", 65), ("Basis / Evidence", "BasisAndEvidence", 360)
        })
            view.Columns.Add(new GridViewColumn { Header = c.Header, DisplayMemberBinding = new System.Windows.Data.Binding(c.Property), Width = c.Width });
        list.View = view; list.Background = (System.Windows.Media.Brush)Application.Current.Resources["Panel2"];
        Grid.SetRow(list, 2); root.Children.Add(list);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var run = new Button { Content = "Run Precision Test", Width = 145, Height = 36, Margin = new Thickness(0, 0, 8, 0) };
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
            summary.Text = "RUNNING — building a temporary SQLite snapshot, indexing identity anchors, auditing the full library, and checking independent positive/negative relationship cases. Production data is read-only.";
            result = await System.Threading.Tasks.Task.Run(() => service.Run(30));
            list.ItemsSource = result.Rows;
            summary.Text = $"Catalog: {result.CatalogSize:N0} • Cases: {result.Cases} • Passed: {result.Passed} • Failed: {result.Failed} • " +
                           $"Candidates: {result.CandidatePairs:N0} • Reduction: {result.CandidateReductionPercent:F2}% • Avg/model: {result.AverageCandidatesPerModel:F2} • " +
                           $"Max: {result.MaxCandidates:N0} • Runtime: {result.Elapsed.TotalSeconds:F1}s • " +
                           (result.Failed == 0 ? "ALL PRECISION CASES PASSED." : "PRECISION FAILURES DETECTED — open the report.");
        }
        catch (System.Exception ex)
        {
            summary.Text = "Relationship precision challenge failed: " + ex.Message;
        }
        finally
        {
            Mouse.OverrideCursor = null;
            if (runButton is not null) runButton.IsEnabled = true;
        }
    }

    private void OpenReport()
    {
        if (result is null) return;
        try { Process.Start(new ProcessStartInfo(result.ReportPath) { UseShellExecute = true }); } catch { }
    }
}
