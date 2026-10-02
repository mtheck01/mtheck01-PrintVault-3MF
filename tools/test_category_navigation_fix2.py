from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
c = (ROOT / 'src/PrintVault/MainWindow.xaml.cs').read_text(encoding='utf-8')
checks = {
    'sidebar category manager returns selection': 'SelectedCategory' in c and 'ShowSelectedModels' in c,
    'sidebar category applies filter': 'categoryFilter = w.SelectedCategory' in c,
    'sidebar category refreshes view': 'ModelView.Refresh()' in c and 'UpdateGalleryPage()' in c,
    'all categories remains escape hatch': 'All Categories' in c,
    'native category selector exists': 'new Popup' in c and 'new ListBox' in c,
    'category selector is directly populated': 'ItemsSource = categories' in c,
    'category selector handles selection': 'categoryList.SelectionChanged' in c and 'selectedCategory = value' in c,
    'no message-only categories action': 'MessageBox.Show(this, string.Join(Environment.NewLine, categories' not in c,
}
failed = [k for k, v in checks.items() if not v]
if failed:
    raise SystemExit('CATEGORY NAVIGATION FIX2 FAILED: ' + ', '.join(failed))
print(f'CATEGORY NAVIGATION FIX2 PASSED — {len(checks)} checks')
