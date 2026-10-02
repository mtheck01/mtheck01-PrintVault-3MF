from pathlib import Path
import re
root=Path(__file__).resolve().parents[1]
for p in root.glob('src/**/*.cs'):
    s=p.read_text(errors='ignore')
    # Raw string literals are allowed only for intentional runtime-parsed templates.
    sanitized=re.sub(r'""".*?"""', '"RAW_STRING_TEMPLATE"', s, flags=re.S)
    for token in ['<Grid>', '</Grid>', '<TextBlock', '<ControlTemplate', '<ComboBoxItem']:
        assert token not in sanitized, f'{p}: XAML token leaked into executable C# -> {token}'
print('XAML/C# SOURCE INTEGRITY PASSED — intentional raw templates excluded')
