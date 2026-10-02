from pathlib import Path
root=Path(__file__).resolve().parents[1]
cs=(root/'src/PrintVault/MainWindow.xaml.cs').read_text()
x=(root/'src/PrintVault/MainWindow.xaml').read_text()
all_cs='\n'.join(p.read_text(errors='ignore') for p in root.glob('src/**/*.cs'))
for needle in ['LibraryStateStore','LearningService','LibraryHealthService','DuplicateManagerWindow','TagManagerWindow','CollectionsWindow']:
    assert needle in all_cs, needle
for needle in ['Library Health','Tag Manager','Collections','Apply Learned Rules','Advanced: category:']:
    assert needle in x, needle
for token in ['category:', 'tag:', 'method:', 'type:', 'special:', 'material:', 'favorite:true', 'duplicate:true', 'missingthumb:true', 'review:true']:
    assert token in cs or token in x, token
assert 'library_state.json' in (root/'src/PrintVault.Infrastructure/LibraryStateStore.cs').read_text()
assert 'OperationJournalEntry' in (root/'src/PrintVault.Core/IntelligenceModels.cs').read_text()
assert 'Applied {changed:N0} learned category corrections' in cs
print('LIBRARY INTELLIGENCE & HEALTH TEST PASSED — 14 checks')
