using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PrintVault.Core;
using PrintVault.Infrastructure;

namespace PrintVault;

public sealed class ModelIntelligenceHueForgeChallengeWindow : Window
{
    private readonly HueForgeContextChallengeService service;
    private readonly TextBlock summary = new();
    private readonly ListView list = new();
    private Button? runButton;
    private HueForgeContextChallengeResult? result;

    public ModelIntelligenceHueForgeChallengeWindow(LibraryRepository repository)
    {
        service = new HueForgeContextChallengeService(repository);
        Title = "HueForge Context Challenge";
        Width = 1550; Height = 850; MinWidth = 1200; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)Application.Current.Resources["Panel"];
        Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Text"];

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock { Text = "HUEFORGE CONTEXT INTELLIGENCE CHALLENGE", FontSize = 22, FontWeight = FontWeights.Bold };
        Grid.SetRow(title, 0); root.Children.Add(title);
        summary.Margin = new Thickness(0, 8, 0, 12); summary.TextWrapping = TextWrapping.Wrap;
        summary.Text = "Validates that 200x200 is treated as HueForge format/size context, not model identity. Printer metadata is contextual only. Production data and physical 3MF files are protected.";
        Grid.SetRow(summary, 1); root.Children.Add(summary);

        var view = new GridView();
        foreach (var c in new (string Header, string Property, double Width)[]
        {
            ("Result", "Result", 70), ("Test Type", "CaseType", 220), ("File A", "ModelA", 270), ("File B", "ModelB", 270),
            ("Size A", "SizeA", 80), ("Size B", "SizeB", 80), ("Expected", "ExpectedBehavior", 210),
            ("Test Basis", "ExpectedBasis", 330), ("Actual", "ActualRelationship", 140), ("Score", "Score", 60), ("Evidence", "Evidence", 420)
        })
            view.Columns.Add(new GridViewColumn { Header = c.Header, DisplayMemberBinding = new System.Windows.Data.Binding(c.Property), Width = c.Width });
        list.View = view; list.Background = (System.Windows.Media.Brush)Application.Current.Resources["Panel2"];
        Grid.SetRow(list, 2); root.Children.Add(list);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var run = new Button { Content = "Run HueForge Test", Width = 145, Height = 36, Margin = new Thickness(0, 0, 8, 0) };
        runButton = run; run.Click += async (_, _) => await RunAsync();
        var report = new Button { Content = "Open Report", Width = 120, Height = 36, Margin = new Thickness(0, 0, 8, 0) }; report.Click += (_, _) => OpenReport();
        var close = new Button { Content = "Close", Width = 100, Height = 36 }; close.Click += (_, _) => Close();
        buttons.Children.Add(run); buttons.Children.Add(report); buttons.Children.Add(close);
        Grid.SetRow(buttons, 3); root.Children.Add(buttons);
        Content = root;
        Loaded += async (_, _) => await RunAsync();
    }

    private async Task RunAsync()
    {
        if (runButton is not null) runButton.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            summary.Text = "Running HueForge context challenge… Testing same-entity separate-file variants, 200x200 size-only pairs, non-HueForge size controls, and printer-only metadata.";
            result = await Task.Run(() => service.Run(36));
            list.ItemsSource = result.Rows;
            var hardFails = result.Checks.Count(x => x.StartsWith("FAIL", System.StringComparison.OrdinalIgnoreCase));
            var negativeControls = result.Rows.Count(x => !x.ExpectedBehavior.Contains("likely variant", System.StringComparison.OrdinalIgnoreCase));
            summary.Text = $"Catalog: {result.CatalogSize:N0} • Cases: {result.Cases} • Passed: {result.Passed} • Failed: {result.Failed} • Negative controls: {negativeControls} • " +
                           (hardFails == 0 ? "ALL HARD CHECKS PASSED." : $"{hardFails} HARD CHECK(S) FAILED — open the report.");
        }
        catch (System.Exception ex)
        {
            summary.Text = "HueForge challenge failed: " + ex.Message;
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
