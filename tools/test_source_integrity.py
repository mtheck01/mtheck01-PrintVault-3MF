from pathlib import Path
root = Path(__file__).parents[1]
cs_files = list((root / "src").rglob("*.cs"))
for p in cs_files:
    text = p.read_text(errors="replace")
    # XAML is allowed only inside the intended raw template in MainWindow.xaml.cs;
    # reject standalone markup accidentally injected into ordinary C# files.
    if p.name != "MainWindow.xaml.cs":
        assert "<ControlTemplate" not in text and "<Grid>" not in text, f"raw XAML leaked into {p}"
main = (root / "src/PrintVault/MainWindow.xaml.cs").read_text()
category = main
assert 'Popup' in category and 'ListBox' in category and 'SelectionChanged' in category
assert 'CreateReadableComboBoxTemplate' not in category
assert '<ControlTemplate' not in category and '<Grid>' not in category
print(f"SOURCE INTEGRITY TEST PASSED — {len(cs_files)} C# files checked")
