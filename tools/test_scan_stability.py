from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
code = (ROOT / "src/PrintVault/MainWindow.xaml.cs").read_text()

checks = {
    "no CollectionView.DeferRefresh scan mutation": "using (ModelView.DeferRefresh())" not in code,
    "bulk collection type": "BulkObservableCollection<ModelRecord> ModelsSource" in code,
    "single reset replacement": "ModelsSource.ReplaceAll(rows)" in code,
    "scan serialization gate": "scanGate.Wait(0)" in code and "scanGate.Release()" in code,
    "active scan cancellation": "scanCts?.Cancel();" in code,
    "selection cleared before replacement": "Models.UnselectAll();" in code,
    "selection restoration": "FirstOrDefault(x => string.Equals(x.Path, selectedModel.Path" in code,
    "cancellation checked before UI replacement": "token.ThrowIfCancellationRequested();" in code,
}
failed = [name for name, ok in checks.items() if not ok]
if failed:
    print("SCAN STABILITY TEST FAILED")
    for name in failed: print("-", name)
    raise SystemExit(1)
print(f"SCAN STABILITY STATIC TEST PASSED — {len(checks)} checks")
