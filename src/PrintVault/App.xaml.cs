using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using WpfApplication = System.Windows.Application;

namespace PrintVault;

public partial class App : WpfApplication
{
    private string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PrintVault");

    private string StartupLog => Path.Combine(LogDirectory, "startup.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        WriteStartup("OnStartup entered");

        DispatcherUnhandledException += (_, args) =>
        {
            WriteStartup("DispatcherUnhandledException: " + args.Exception);
            try
            {
                MessageBox.Show(args.Exception.ToString(), "PrintVault Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteStartup("UnhandledException: " + args.ExceptionObject);

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteStartup("UnobservedTaskException: " + args.Exception);
            args.SetObserved();
        };

        try
        {
            base.OnStartup(e);
            WriteStartup("Base OnStartup completed");

            var window = new MainWindow();
            MainWindow = window;
            WriteStartup("MainWindow constructed");

            window.Show();
            WriteStartup("MainWindow shown");
        }
        catch (Exception ex)
        {
            WriteStartup("STARTUP FAILURE: " + ex);
            try
            {
                MessageBox.Show(ex.ToString(), "PrintVault Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            Shutdown(1);
        }
    }

    private void WriteStartup(string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(StartupLog, $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch { }
    }
}
