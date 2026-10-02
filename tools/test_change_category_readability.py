from pathlib import Path
p=Path(__file__).parents[1]/"src/PrintVault/MainWindow.xaml.cs"
x=p.read_text(encoding="utf-8")
checks={
    "native popup": "new Popup" in x,
    "native list": "new ListBox" in x,
    "dark list background": "Background = panelBrush" in x,
    "category source": "ItemsSource = categories" in x,
    "selected category state": "selectedCategory = value" in x,
    "selector updates text": "selectorButton.Content = selectedCategory" in x,
    "popup placement": "PlacementMode.Bottom" in x,
    "custom entry retained": "Type a new custom category" in x,
    "old fragile template removed": "CreateReadableComboBoxTemplate" not in x,
}
failed=[k for k,v in checks.items() if not v]
assert not failed, f"failed: {failed}"
print(f"CHANGE CATEGORY READABILITY TEST PASSED — {len(checks)} checks")
