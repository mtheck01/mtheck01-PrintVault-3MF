from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
main = (ROOT / "src/PrintVault/MainWindow.xaml.cs").read_text(encoding="utf-8")
repo = (ROOT / "src/PrintVault.Infrastructure/LibraryRepository.cs").read_text(encoding="utf-8")
geometry = (ROOT / "src/PrintVault.Infrastructure/ComponentAwareGeometryResolver.cs").read_text(encoding="utf-8")
state = (ROOT / "src/PrintVault.Infrastructure/LibraryStateStore.cs").read_text(encoding="utf-8")
migration = (ROOT / "src/PrintVault.Infrastructure/MigrationService.cs").read_text(encoding="utf-8")

checks = {
    "startup does not auto choose library": "ChooseRoot();" not in main.split("private void Window_Loaded",1)[1].split("private string? LoadRoot",1)[0],
    "startup no-library status": 'No library selected. Choose Settings / Library' in main,
    "clear resets root": "root = null;" in main,
    "clear resets organization": "organization = null;" in main,
    "reset rebuild does not clear live catalog first": "engine.Repository.ClearAllModels();" not in main.split("private async void ResetAndRebuildLibrary_Click",1)[1].split("private void Scan_Click",1)[0],
    "reset rebuild uses atomic clean candidate": "preserveMetadata: false" in main,
    "reset still creates recovery backup": "engine.Repository.BackupDatabase();" in main,
    "repository clear method exists": "public void ClearAllModels()" in repo,
    "production library remains blocked": "IsForbiddenProductionLibrary" in main,
    "component resolver uses recursion stack not global visited state": "seen.Remove(key);" in geometry and "finally" in geometry,
    "corrupt state is preserved instead of silently reset": ".corrupt." in state and "return new();" in state,
    "legacy migration failures are surfaced": "Legacy library migration failed" in migration and "catch (Exception ex)" in migration,
}
failed = [k for k,v in checks.items() if not v]
for k,v in checks.items(): print(("PASS: " if v else "FAIL: ")+k)
print("RESULT:", "PASS" if not failed else "FAIL")
raise SystemExit(0 if not failed else 1)
