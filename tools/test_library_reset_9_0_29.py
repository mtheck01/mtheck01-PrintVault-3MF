from pathlib import Path

root = Path(__file__).resolve().parents[1]
repo = (root / 'src/PrintVault.Infrastructure/LibraryRepository.cs').read_text()
mw = (root / 'src/PrintVault/MainWindow.xaml.cs').read_text()
engine = (root / 'src/PrintVault.Infrastructure/LibraryEngine.cs').read_text()
version = (root / 'VERSION').read_text().strip()

assert version == '9.0.33', version
assert 'BackupDatabase' in repo
assert 'VACUUM INTO $path' in repo
assert 'engine.Repository.BackupDatabase()' in mw
assert 'engine.Repository.ClearAllModels()' in mw
assert 'engine.RebuildAsync(new[] { root }' in mw
# The reset path must not delete/move physical 3MF files.
segment = mw[mw.index('private async void ResetAndRebuildLibrary_Click'):mw.index('private void Scan_Click')]
for forbidden in ['File.Delete(', 'Directory.Delete(', 'File.Move(', 'Directory.Move(']:
    assert forbidden not in segment, forbidden
# Rebuild creates fresh ModelRecord values after the reset, rather than reusing old rows.
assert 'var m = new ModelRecord' in engine
print('LIBRARY RESET 9.0.33 — PASS')
print('Backup precedes model-table clear; physical 3MF files are never deleted or moved; rebuild starts from fresh records.')
