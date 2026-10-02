from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
code = (ROOT / "src/PrintVault/MainWindow.xaml.cs").read_text(encoding="utf-8")

checks = [
    ("class CategoryWindow" in code, "CategoryWindow exists"),
    ("new Popup" in code, "native WPF Popup is used"),
    ("new ListBox" in code, "native WPF ListBox is used for category items"),
    ("ItemsSource = categories" in code, "category list receives the category collection"),
    ("PlacementMode.Bottom" in code, "popup is placed below selector"),
    ("categoryList.SelectionChanged" in code, "selection is handled"),
    ("selectedCategory = value" in code, "selected category is persisted in dialog state"),
    ("selectorButton.Content = selectedCategory" in code, "selector displays selected category"),
    ("Template = CreateReadableComboBoxTemplate()" not in code, "fragile custom ComboBox template removed"),
]
failed = [msg for ok, msg in checks if not ok]
if failed:
    print("CATEGORY DROPDOWN FIX3 FAILED")
    for msg in failed: print("-", msg)
    raise SystemExit(1)
print(f"CATEGORY DROPDOWN FIX3 PASSED — {len(checks)} checks")
