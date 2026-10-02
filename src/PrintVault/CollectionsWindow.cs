using System;
using System.Collections.Generic;
using System.Linq;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PrintVault.Core;
using PrintVault.Infrastructure;

namespace PrintVault;

public sealed class CollectionsWindow : Window
{
    private readonly LibraryStateStore state; private readonly IReadOnlyList<ModelRecord> models; private readonly IReadOnlyList<ModelRecord> selected; private readonly ListBox list=new(); private readonly Action refreshMain;
    public CollectionsWindow(LibraryStateStore state,IReadOnlyList<ModelRecord> models,IReadOnlyList<ModelRecord> selected,Action refreshMain){this.state=state;this.models=models;this.selected=selected;this.refreshMain=refreshMain;Title="PrintVault — Collections";Width=760;Height=620;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(9,17,31));Foreground=Brushes.White;var root=new DockPanel{Margin=new Thickness(20)};var head=new TextBlock{Text="COLLECTIONS",FontSize=25,FontWeight=FontWeights.Bold,Foreground=new SolidColorBrush(Color.FromRgb(66,165,255))};DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);list.Background=new SolidColorBrush(Color.FromRgb(16,27,45));list.BorderThickness=new Thickness(0);root.Children.Add(list);var bar=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(bar,Dock.Bottom);root.Children.Add(bar);bar.Children.Add(B("New Collection",NewCollection));bar.Children.Add(B("Delete",Delete));bar.Children.Add(B("Add Selection",AddSelection));bar.Children.Add(B("Show Files",Show));bar.Children.Add(B("Close",(s,e)=>Close()));Content=root;Loaded+=(s,e)=>Load();}
    private Button B(string t,RoutedEventHandler h){var b=new Button{Content=t,Margin=new Thickness(4),Padding=new Thickness(14,9,14,9),Foreground=Brushes.White,Background=new SolidColorBrush(Color.FromRgb(22,37,59))};b.Click+=h;return b;}
    private void Load(object? s=null,RoutedEventArgs? e=null){list.Items.Clear();foreach(var c in state.Collections)list.Items.Add(new ListBoxItem{Content=$"{c.Name}   •   {c.Paths.Count:N0} models",Tag=c.Name});}
    private void NewCollection(object? s,RoutedEventArgs e){var w=new TextInputWindow("New Collection","Collection name",""){Owner=this};if(w.ShowDialog()==true&&!string.IsNullOrWhiteSpace(w.Value)){state.CreateCollection(w.Value);Load();}}
    private void Delete(object? s,RoutedEventArgs e){if(list.SelectedItem is not ListBoxItem i)return;var n=i.Tag?.ToString();if(MessageBox.Show(this,$"Delete collection '{n}'? Files will not be moved or deleted.","Delete Collection",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes){state.DeleteCollection(n??"");Load();}}
    private void AddSelection(object? s,RoutedEventArgs e){if(list.SelectedItem is not ListBoxItem i)return;var n=i.Tag?.ToString()??"";state.SetCollectionMembers(n,state.GetCollectionMembers(n).Concat(selected.Select(x=>x.Path)));Load();}
    private void Show(object? s,RoutedEventArgs e){if(list.SelectedItem is not ListBoxItem i)return;var n=i.Tag?.ToString()??"";var paths=state.GetCollectionMembers(n);var count=models.Count(x=>paths.Contains(x.Path));MessageBox.Show(this,$"Collection: {n}\n\n{count:N0} current library models are members.\n\nCollections are virtual and do not move files.","Collection",MessageBoxButton.OK,MessageBoxImage.Information);}
}
