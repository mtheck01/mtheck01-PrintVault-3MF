from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
code = (ROOT / "src/PrintVault/MainWindow.xaml.cs").read_text()
engine_code = (ROOT / "src/PrintVault.Infrastructure/LibraryEngine.cs").read_text()
discovery_code = (ROOT / "src/PrintVault.Infrastructure/LibraryFileDiscovery.cs").read_text()

checks = {
    "no CollectionView.DeferRefresh scan mutation": "using (ModelView.DeferRefresh())" not in code,
    "bulk collection type": "BulkObservableCollection<ModelRecord> ModelsSource" in code,
    "single reset replacement": "ModelsSource.ReplaceAll(rows)" in code,
    "scan serialization gate": "scanGate.Wait(0)" in code and "scanGate.Release()" in code,
    "active scan cancellation": "scanCts?.Cancel();" in code,
    "selection cleared before replacement": "Models.UnselectAll();" in code,
    "selection restoration": "FirstOrDefault(x => string.Equals(x.Path, selectedModel.Path" in code,
    "cancellation checked before UI replacement": "token.ThrowIfCancellationRequested();" in code,
    "scan failure reloads committed catalog record": "existing.ContainsKey(path) && File.Exists(path)" in engine_code and "result[i] = repo.Get(path);" in engine_code,
    "scan failure only preserves existing file": "existing.TryGetValue(path, out var previous) && File.Exists(path)" in engine_code,
    "scan reports failure instead of deleting record": "Interlocked.Increment(ref failed);" in engine_code and "repo.SaveAll(models, stale);" in engine_code,
    "rebuild fails closed before catalog commit": "if (failed > 0)" in engine_code and "The existing catalog was left unchanged" in engine_code,
    "rebuild requires full discovered-file count": "persisted.Count != models.Count || persisted.Count != files.Count" in engine_code,
    "filesystem enumeration fails closed": "scan was stopped to prevent an incomplete library from being treated as authoritative" in discovery_code and "throw new IOException" in discovery_code,
    "filesystem discovery does not silently swallow enumeration errors": "catch { files = Array.Empty<string>(); }" not in discovery_code and "catch { dirs = Array.Empty<string>(); }" not in discovery_code,
}
failed = [name for name, ok in checks.items() if not ok]
if failed:
    print("SCAN STABILITY TEST FAILED")
    for name in failed: print("-", name)
    raise SystemExit(1)
print(f"SCAN STABILITY STATIC TEST PASSED — {len(checks)} checks")
