using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using PrintVault.Core;
using PrintVault.Infrastructure;

namespace PrintVault;

public sealed class SemanticEvidenceFusionChallengeWindow : Window
{
    private readonly LibraryRepository repository;
    private readonly TextBlock status = new();
    private readonly ProgressBar progress = new();
    private readonly TextBlock stage = new();
    private readonly DataGrid results = new();
    private readonly Button run = new();
    private readonly Button open = new();
    private SemanticEvidenceFusionChallengeResult? last;

    public SemanticEvidenceFusionChallengeWindow(LibraryRepository repository)
    {
        this.repository = repository;
        Title = "PrintVault — Semantic Evidence Fusion";
        Width = 1180; Height = 760; MinWidth = 980; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.White;

        var root = new DockPanel { Margin = new Thickness(18) };
        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = "SEMANTIC EVIDENCE FUSION", FontSize = 24, FontWeight = FontWeights.Bold });
        head.Children.Add(new TextBlock { Text = "Combines independent semantic evidence without writing classifications or physical 3MF files.", Margin = new Thickness(0, 4, 0, 12) });
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);

        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        run.Content = "Run Fusion Audit"; run.Padding = new Thickness(14, 8, 14, 8); run.Click += Run_Click;
        open.Content = "Open Report"; open.Padding = new Thickness(14, 8, 14, 8); open.Margin = new Thickness(8, 0, 0, 0); open.IsEnabled = false; open.Click += (_, _) => { if (last is not null && File.Exists(last.ReportPath)) Process.Start(new ProcessStartInfo(last.ReportPath) { UseShellExecute = true }); };
        controls.Children.Add(run); controls.Children.Add(open);
        status.Margin = new Thickness(14, 0, 0, 0); controls.Children.Add(status);
        DockPanel.SetDock(controls, Dock.Top); root.Children.Add(controls);

        stage.Margin = new Thickness(0, 10, 0, 3); DockPanel.SetDock(stage, Dock.Top); root.Children.Add(stage);
        progress.Minimum = 0; progress.Maximum = 100; progress.Height = 8; progress.Margin = new Thickness(0, 0, 0, 12); DockPanel.SetDock(progress, Dock.Top); root.Children.Add(progress);

        results.AutoGenerateColumns = true; results.IsReadOnly = true; results.CanUserAddRows = false; root.Children.Add(results);
        Content = root;
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        run.IsEnabled = false; open.IsEnabled = false; results.ItemsSource = null;
        status.Text = "RUNNING"; stage.Text = "Starting…"; progress.Value = 0;
        try
        {
            var service = new SemanticEvidenceFusionChallengeService(repository);
            last = await System.Threading.Tasks.Task.Run(() => service.Run((current, total, message) => Dispatcher.Invoke(() =>
            {
                progress.Value = total == 0 ? 0 : current * 100d / total;
                stage.Text = message;
            })));
            status.Text = $"{last.Passed} passed • {last.Failed} failed • {last.Unavailable} unavailable • {last.Processed:N0} processed • {last.Elapsed.TotalSeconds:F2}s";
            progress.Value = 100;
            results.ItemsSource = last.Rows;
            open.IsEnabled = true;
        }
        catch (Exception ex)
        {
            status.Text = "FAILED"; stage.Text = ex.Message;
            MessageBox.Show(this, ex.ToString(), "Semantic Evidence Fusion", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { run.IsEnabled = true; }
    }
}
