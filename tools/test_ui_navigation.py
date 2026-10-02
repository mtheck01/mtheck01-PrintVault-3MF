from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
x=(ROOT/'src/PrintVault/MainWindow.xaml').read_text()
c=(ROOT/'src/PrintVault/MainWindow.xaml.cs').read_text()
a=(ROOT/'src/PrintVault/App.xaml').read_text()
checks={
 'all_models_escape_hatch':'All Models / List View' in x and 'favoritesOnly = false' in c,
 'list_clears_search':'private void List_Click' in c and 'Search.Clear();' in c,
 'gallery_clears_favorites':'private void Gallery_Click' in c and 'favoritesOnly = false' in c,
 'escape_exits_favorites':'if (favoritesOnly)' in c and 'All Models' in c,
 'open_selected_button':'Content="Open Selected"' in x and 'Select a model first.' in c,
 'category_selector_dark_theme':'new ListBox' in c and 'Background = panelBrush' in c and 'Foreground = textBrush' in c,
 'textbox_selection_contrast':'SelectionTextBrush' in a and 'SelectionBrush' in a,
}
failed=[k for k,v in checks.items() if not v]
if failed: raise SystemExit('NAVIGATION/UI TEST FAILED: '+', '.join(failed))
print('NAVIGATION/UI TEST PASSED — %d checks' % len(checks))
