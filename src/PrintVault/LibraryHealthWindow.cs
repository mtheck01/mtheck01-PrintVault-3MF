using System;
using System.Collections.Generic;
using System.Linq;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PrintVault.Core;
using PrintVault.Infrastructure;

namespace PrintVault;

public sealed class LibraryHealthWindow : Window
{
    private readonly LibraryHealthService health;
    private readonly LibraryStateStore state;
    private readonly Action openDuplicates;
    private readonly Action openTags;
    private readonly Action openCollections;
    private readonly TextBlock details = new();
    private readonly ListBox history = new();

    public LibraryHealthWindow(LibraryHealthService health, LibraryStateStore state, Action openDuplicates, Action openTags, Action openCollections)
    {
        this.health=health; this.state=state; this.openDuplicates=openDuplicates; this.openTags=openTags; this.openCollections=openCollections;
        Title="PrintVault — Library Health"; Width=820; Height=650; WindowStartupLocation=WindowStartupLocation.CenterOwner; Background=new SolidColorBrush(Color.FromRgb(9,17,31)); Foreground=Brushes.White;
        var root=new DockPanel { Margin=new Thickness(24) };
        var title=new TextBlock { Text="LIBRARY HEALTH", FontSize=28, FontWeight=FontWeights.Bold, Foreground=new SolidColorBrush(Color.FromRgb(66,165,255)) }; DockPanel.SetDock(title,Dock.Top); root.Children.Add(title);
        details.Margin=new Thickness(0,20,0,10); details.TextWrapping=TextWrapping.Wrap; details.FontSize=15; root.Children.Add(details);
        history.Height=130; history.Background=new SolidColorBrush(Color.FromRgb(16,27,45)); history.BorderThickness=new Thickness(0); history.Margin=new Thickness(0,0,0,12); root.Children.Add(history);
        var buttons=new WrapPanel(); DockPanel.SetDock(buttons,Dock.Bottom);
        buttons.Children.Add(B("Review Duplicates",()=>openDuplicates())); buttons.Children.Add(B("Tag Manager",()=>openTags())); buttons.Children.Add(B("Collections",()=>openCollections())); buttons.Children.Add(B("Refresh",Refresh));
        root.Children.Add(buttons); Content=root; Loaded+=(s,e)=>Refresh();
    }
    private Button B(string text,Action a){var b=new Button{Content=text,Margin=new Thickness(5),Padding=new Thickness(16,10,16,10),Background=new SolidColorBrush(Color.FromRgb(22,37,59)),Foreground=Brushes.White};b.Click+=(s,e)=>a();return b;}
    private void Refresh(){var cats=new[] {"01_Needs_Review","02_Functional","02_Household","03_Automotive","03_Decor","04_Figures","05_Game_Models","05_Gaming","06_Cosplay","06_Decorative","07_Automotive","07_Multi_Color","08_Aviation","08_Test_Print","09_Aircraft","09_Models","10_Multi_Color","11_Test_Print","99_Other"};var s=health.Analyze(cats);details.Text=$"Models: {s.Models:N0}\nFavorites: {s.Favorites:N0}\nDuplicate groups: {s.DuplicateGroups:N0} ({s.DuplicateFiles:N0} files)\nDuplicate storage: {s.DuplicateBytes/1024d/1024d/1024d:0.00} GB\nNeeds review: {s.NeedsReview:N0}\nMissing thumbnails: {s.MissingThumbnails:N0}\nLegacy categories: {s.LegacyCategories:N0} ({s.EmptyLegacyCategories:N0} empty)\nCollections: {s.Collections:N0}\nLearned correction rules: {s.LearnedRules:N0}\nOperation history entries: {s.JournalEntries:N0}\nLibrary size: {s.Bytes/1024d/1024d/1024d:0.00} GB"; history.Items.Clear(); foreach(var j in state.Journal.Take(12)) history.Items.Add($"{j.Utc.ToLocalTime():g} • {j.Operation} • {j.Summary}");}
}
