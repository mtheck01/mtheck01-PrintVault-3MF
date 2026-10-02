using System;
using System.Collections.Generic;
using System.Linq;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PrintVault.Core;
using PrintVault.Infrastructure;

namespace PrintVault;

public sealed class TagManagerWindow : Window
{
    private readonly IReadOnlyList<ModelRecord> models; private readonly LibraryRepository repo; private readonly Action refreshMain; private readonly ListBox tags=new();
    public TagManagerWindow(IReadOnlyList<ModelRecord> models,LibraryRepository repo,Action refreshMain){this.models=models;this.repo=repo;this.refreshMain=refreshMain;Title="PrintVault — Tag Manager";Width=700;Height=620;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(9,17,31));Foreground=Brushes.White;var root=new DockPanel{Margin=new Thickness(20)};var title=new TextBlock{Text="TAG MANAGER",FontSize=25,FontWeight=FontWeights.Bold,Foreground=new SolidColorBrush(Color.FromRgb(66,165,255))};DockPanel.SetDock(title,Dock.Top);root.Children.Add(title);tags.Background=new SolidColorBrush(Color.FromRgb(16,27,45));tags.BorderThickness=new Thickness(0);root.Children.Add(tags);var bar=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(bar,Dock.Bottom);root.Children.Add(bar);bar.Children.Add(B("Merge / Rename",Merge));bar.Children.Add(B("Refresh",Load));bar.Children.Add(B("Close",(s,e)=>Close()));Content=root;Loaded+=(s,e)=>Load();}
    private Button B(string t,RoutedEventHandler h){var b=new Button{Content=t,Margin=new Thickness(4),Padding=new Thickness(14,9,14,9),Foreground=Brushes.White,Background=new SolidColorBrush(Color.FromRgb(22,37,59))};b.Click+=h;return b;}
    private void Load(object? s=null,RoutedEventArgs? e=null){tags.Items.Clear();foreach(var g in models.SelectMany(x=>x.Tags.Split(new[]{',',';','|','\n'},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)).GroupBy(x=>x,StringComparer.OrdinalIgnoreCase).OrderByDescending(g=>g.Count()).ThenBy(g=>g.Key,StringComparer.OrdinalIgnoreCase)){tags.Items.Add(new ListBoxItem{Content=$"{g.Key}   •   {g.Count():N0} models",Tag=g.Key});}}
    private void Merge(object? s,RoutedEventArgs e){if(tags.SelectedItem is not ListBoxItem i)return;var old=i.Tag?.ToString();var w=new TextInputWindow("Merge Tag","Replace selected tag with",old??""){Owner=this};if(w.ShowDialog()!=true)return;var target=w.Value.Trim();if(target.Length==0||string.Equals(old,target,StringComparison.OrdinalIgnoreCase))return;foreach(var m in models){var parts=m.Tags.Split(new[]{',',';','|','\n'},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(x=>string.Equals(x,old,StringComparison.OrdinalIgnoreCase)?target:x).Distinct(StringComparer.OrdinalIgnoreCase);m.Tags=string.Join(", ",parts);repo.Upsert(m);}refreshMain();Load();MessageBox.Show(this,$"Merged '{old}' into '{target}'.","Tag Manager",MessageBoxButton.OK,MessageBoxImage.Information);}
}
