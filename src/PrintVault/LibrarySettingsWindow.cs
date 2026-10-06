using PrintVault.Core;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace PrintVault;

public sealed class LibrarySettingsWindow : Window
{
    public string? SelectedLibrary { get; private set; }
    public bool ClearRequested { get; private set; }

    public LibrarySettingsWindow(string current)
    {
        Title = $"PrintVault {AppVersion.Version} — Library Settings";
        Width = 620; Height = 270; MinWidth = 620; MinHeight = 270;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(20, 34, 56));
        Foreground = Brushes.White;

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = "LIBRARY SELECTION", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,12) });
        root.Children.Add(new TextBlock { Text = "Current library", Foreground = new SolidColorBrush(Color.FromRgb(170,185,205)) });
        root.Children.Add(new TextBlock { Text = current, Margin = new Thickness(0,4,0,18), TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock { Text = $"The {AppVersion.Version} Engineering build will not select or scan D:\\3d print files.", Foreground = new SolidColorBrush(Color.FromRgb(255,205,120)), Margin = new Thickness(0,0,0,18) });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var change = new Button { Content = "Change Library", Width = 125, Margin = new Thickness(0,0,8,0), Padding = new Thickness(10,7,10,7) };
        var clear = new Button { Content = "Clear Library", Width = 115, Margin = new Thickness(0,0,8,0), Padding = new Thickness(10,7,10,7) };
        var cancel = new Button { Content = "Cancel", Width = 90, Padding = new Thickness(10,7,10,7) };
        change.Click += (_,__) => Choose();
        clear.Click += (_,__) => { ClearRequested = true; DialogResult = true; };
        cancel.Click += (_,__) => DialogResult = false;
        buttons.Children.Add(change); buttons.Children.Add(clear); buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        Content = root;
    }

    private void Choose()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Choose your 3MF library folder",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(@"D:\3d print files test") ? @"D:\3d print files test" : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            SelectedLibrary = dialog.SelectedPath;
            DialogResult = true;
        }
    }
}
