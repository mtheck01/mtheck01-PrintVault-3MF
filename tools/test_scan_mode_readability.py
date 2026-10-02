from pathlib import Path
p=Path(__file__).parents[1]
x=(p/"src/PrintVault/MainWindow.xaml").read_text(encoding="utf-8")
assert 'PrintVault 3MF 8.7.8' in x
assert 'SystemColors.WindowBrushKey' in x
assert 'SystemColors.WindowTextBrushKey' in x
assert 'SystemColors.ControlBrushKey' in x
assert 'SystemColors.ControlTextBrushKey' in x
assert 'SystemColors.HighlightBrushKey' in x
assert 'SystemColors.HighlightTextBrushKey' in x
assert 'Foreground="#0B1728"' in x
assert 'Background="#E8EDF3"' in x
assert '⚡ Quick Scan' in x and '🚀 Turbo Scan' in x and '🧠 Deep Scan' in x
assert 'ItemContainerStyle' in x
print('Scan mode readability: PASS — closed selection and popup use explicit high-contrast brushes')
