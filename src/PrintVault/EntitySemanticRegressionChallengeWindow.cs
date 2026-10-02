using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using PrintVault.Core;
using PrintVault.Infrastructure;

namespace PrintVault;

public sealed class EntitySemanticRegressionChallengeWindow : Window
{
    private readonly EntitySemanticRegressionChallengeService service;
    private readonly TextBlock summary;
    private readonly ListView list;
    private EntitySemanticRegressionResult? result;
    private Button? runButton;

    public EntitySemanticRegressionChallengeWindow(LibraryRepository repository)
    {
        service = new EntitySemanticRegressionChallengeService(repository);
        Title = "Entity Semantic Regression Challenge";
        Width = 1650; Height = 850;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)Application.Current.Resources["Panel"];
        Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Text"];

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock { Text = "Entity Semantic Regression V2 — Real Catalog + Controlled Fixtures", FontSize = 22, FontWeight = FontWeights.Bold };
        Grid.SetRow(title, 0); root.Children.Add(title);
        summary = new TextBlock { Margin = new Thickness(0, 8, 0, 12), TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(summary, 1); root.Children.Add(summary);

        list = new ListView { Background = (System.Windows.Media.Brush)Application.Current.Resources["Panel2"] };
        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = "ID", DisplayMemberBinding = new Binding("CaseId"), Width = 70 });
        view.Columns.Add(new GridViewColumn { Header = "Description", DisplayMemberBinding = new Binding("Description"), Width = 230 });
        view.Columns.Add(new GridViewColumn { Header = "Model", DisplayMemberBinding = new Binding("Name"), Width = 290 });
        view.Columns.Add(new GridViewColumn { Header = "Expected Entity", DisplayMemberBinding = new Binding("ExpectedEntity"), Width = 150 });
        view.Columns.Add(new GridViewColumn { Header = "Actual Entity", DisplayMemberBinding = new Binding("ActualEntity"), Width = 150 });
        view.Columns.Add(new GridViewColumn { Header = "Expected Domain", DisplayMemberBinding = new Binding("ExpectedDomain"), Width = 145 });
        view.Columns.Add(new GridViewColumn { Header = "Actual Domain", DisplayMemberBinding = new Binding("ActualDomain"), Width = 145 });
        view.Columns.Add(new GridViewColumn { Header = "Subtype", DisplayMemberBinding = new Binding("ActualSubtype"), Width = 190 });
        view.Columns.Add(new GridViewColumn { Header = "Conf.", DisplayMemberBinding = new Binding("Confidence"), Width = 55 });
        view.Columns.Add(new GridViewColumn { Header = "Result", DisplayMemberBinding = new Binding("Passed"), Width = 65 });
        list.View = view; Grid.SetRow(list, 2); root.Children.Add(list);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var run = new Button { Content = "Run Regression Test", Width = 150, Height = 36, Margin = new Thickness(0, 0, 8, 0) };
        runButton = run; run.Click += async (_, _) => await RunAsync();
        var open = new Button { Content = "Open Report", Width = 120, Height = 36, Margin = new Thickness(0, 0, 8, 0) };
        open.Click += (_, _) => { if (result is not null) { try { Process.Start(new ProcessStartInfo(result.ReportPath) { UseShellExecute = true }); } catch { } } };
        var close = new Button { Content = "Close", Width = 100, Height = 36 }; close.Click += (_, _) => Close();
        buttons.Children.Add(run); buttons.Children.Add(open); buttons.Children.Add(close); Grid.SetRow(buttons, 3); root.Children.Add(buttons);
        Content = root;
        Loaded += async (_, _) => await RunAsync();
    }

    private async Task RunAsync()
    {
        if (runButton is not null) runButton.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            summary.Text = "Running semantic regression V2… Real catalog cases are read-only; controlled fixtures validate mappings independently.";
            result = await Task.Run(() => service.Run());
            list.ItemsSource = result.Rows;
            summary.Text = $"Build {AppVersion.Version} • Catalog: {result.CatalogSize:N0} • Cases: {result.Cases} • Available: {result.Available} • Passed: {result.Passed} • Failed: {result.Failed} • Physical files={(result.PhysicalFilesUnchanged ? "UNCHANGED" : "CHANGED")}. " +
                           (result.Failed == 0 ? "ALL AVAILABLE SEMANTIC REGRESSION CHECKS PASSED." : "REGRESSION FAILURE — open the report.");
        }
        catch (Exception ex) { summary.Text = "Test failed: " + ex.Message; }
        finally { Mouse.OverrideCursor = null; if (runButton is not null) runButton.IsEnabled = true; }
    }
}
