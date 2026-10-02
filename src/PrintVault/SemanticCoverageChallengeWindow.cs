using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using PrintVault.Infrastructure;

namespace PrintVault;

public sealed class SemanticCoverageChallengeWindow : Window
{
    private readonly SemanticCoverageChallengeService service;
    private readonly TextBlock summary = new();
    private readonly ProgressBar progress = new() { Height = 18, Minimum = 0 };
    private readonly ListView list = new() { Background = (System.Windows.Media.Brush)Application.Current.Resources["Panel2"] };
    private Button? runButton;
    private SemanticCoverageChallengeResult? result;

    public SemanticCoverageChallengeWindow(LibraryRepository repository)
    {
        service = new SemanticCoverageChallengeService(repository);
        Title = "Semantic Coverage & Confidence Challenge";
        Width = 1600; Height = 900; MinWidth = 1200; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)Application.Current.Resources["Panel"];
        Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Text"];
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = new TextBlock { Text = "SEMANTIC COVERAGE & CONFIDENCE CHALLENGE", FontSize = 22, FontWeight = FontWeights.Bold };
        Grid.SetRow(title, 0); root.Children.Add(title);
        summary.Margin = new Thickness(0, 8, 0, 8); summary.TextWrapping = TextWrapping.Wrap;
        summary.Text = "Read-only full-library audit of entity coverage, confidence, semantic contradictions, and identity coverage opportunities. No classifications are applied.";
        Grid.SetRow(summary, 1); root.Children.Add(summary);
        Grid.SetRow(progress, 2); root.Children.Add(progress);
        var view = new GridView();
        foreach (var c in new (string Header, string Property, double Width)[]
        {
            ("Index", "Index", 55), ("Name", "Name", 330), ("Entity", "Entity", 180), ("Domain", "Domain", 160),
            ("Entity %", "EntityConfidence", 70), ("Class %", "ClassificationConfidence", 70), ("Coverage", "Coverage", 90), ("Risk", "Risk", 100), ("Evidence", "Evidence", 520)
        }) view.Columns.Add(new GridViewColumn { Header = c.Header, DisplayMemberBinding = new Binding(c.Property), Width = c.Width });
        list.View = view; Grid.SetRow(list, 3); root.Children.Add(list);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var run = new Button { Content = "Run Coverage Audit", Width = 145, Height = 36, Margin = new Thickness(0, 0, 8, 0) };
        runButton = run; run.Click += async (_, _) => await RunAsync();
        var report = new Button { Content = "Open Report", Width = 120, Height = 36, Margin = new Thickness(0, 0, 8, 0) }; report.Click += (_, _) => OpenReport();
        var close = new Button { Content = "Close", Width = 100, Height = 36 }; close.Click += (_, _) => Close();
        buttons.Children.Add(run); buttons.Children.Add(report); buttons.Children.Add(close); Grid.SetRow(buttons, 4); root.Children.Add(buttons);
        Content = root; Loaded += async (_, _) => await RunAsync();
    }

    private async System.Threading.Tasks.Task RunAsync()
    {
        if (runButton is not null) runButton.IsEnabled = false;
        progress.Value = 0; progress.Maximum = 1758; Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            result = await System.Threading.Tasks.Task.Run(() => service.Run((done, total, stage) => Dispatcher.Invoke(() =>
            {
                progress.Maximum = Math.Max(1, total); progress.Value = Math.Min(done, total);
                summary.Text = $"RUNNING — {stage} • {done:N0}/{total:N0} • Production database and physical 3MF files remain read-only.";
            })));
            list.ItemsSource = result.Rows;
            summary.Text = $"Catalog: {result.CatalogSize:N0} • Entities: {result.EntityRecords:N0} • High: {result.HighConfidenceEntities:N0} • Medium: {result.MediumConfidenceEntities:N0} • Low: {result.LowConfidenceRecords:N0} • No entity: {result.NoEntityRecords:N0} • Contradictions: {result.Contradictions:N0} • Opportunities: {result.CoverageOpportunities:N0} • Parity: {result.DeterministicPassed}/{result.DeterministicSamples} • Runtime: {result.Elapsed.TotalSeconds:F1}s";
        }
        catch (Exception ex) { summary.Text = "Semantic coverage audit failed: " + ex.Message; }
        finally { Mouse.OverrideCursor = null; if (runButton is not null) runButton.IsEnabled = true; }
    }
    private void OpenReport() { if (result is null) return; try { Process.Start(new ProcessStartInfo(result.ReportPath) { UseShellExecute = true }); } catch { } }
}
