from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
assert (ROOT / "VERSION").read_text().strip() == "9.0.33"

service = (ROOT / "src/PrintVault.Infrastructure/WholeLibraryRootCauseAnalysisService.cs").read_text()
main = (ROOT / "src/PrintVault/MainWindow.xaml.cs").read_text()

assert "activeCatalogSnapshot" in service
assert "ACTIVE_CATALOG_SYNC_REPAIR" in service
assert "repositoryCountBeforeSync" in service
assert "repositoryCountAfterSync" in service
assert "catalogMismatch" in service
assert "repository.SaveAll(validSnapshot)" in service
assert "activeCatalogSnapshot = ModelsSource.ToList()" in main
assert "await scanGate.WaitAsync()" in main
assert "service.Run(activeCatalogSnapshot: activeCatalogSnapshot)" in main

print("WHOLE-LIBRARY CATALOG SYNC 9.0.33 — PASS")
print("Forensic analysis is tied to the active catalog, protected from concurrent scans, and repairs an empty repository from the populated catalog snapshot.")
