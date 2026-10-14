from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
program = (ROOT / "tools/PrintVault.Automation/Program.cs").read_text(encoding="utf-8")
self_test = (ROOT / "tools/PrintVault.Automation/CategoryRegistrySelfTest.cs").read_text(encoding="utf-8")
store = (ROOT / "src/PrintVault.Infrastructure/CategoryRegistryStore.cs").read_text(encoding="utf-8")
main_window = (ROOT / "src/PrintVault/MainWindow.xaml.cs").read_text(encoding="utf-8")
organization = (ROOT / "src/PrintVault.Infrastructure/OrganizationService.cs").read_text(encoding="utf-8")

assert '"selftest-category-registry"' in program
assert "CATEGORY_REGISTRY_SELFTEST=PASS" in self_test
for behavior in (
    "missing registry should load as empty",
    "deduplicate case-insensitively",
    "removal should report the removed entry",
    "failed save must leave the previous registry intact",
):
    assert behavior in self_test, f"missing category registry regression check: {behavior}"
assert "private readonly CategoryRegistryStore categoryRegistry = new();" in main_window
assert "categoryRegistry.Load()" in main_window
assert "categoryRegistry.Save(categories)" in main_window
assert "categoryRegistry.Remove(legacy)" in organization
assert "categoryRegistry.Count(legacy)" in organization
assert "File.Move(temporaryPath, path, true)" in store

print("Category registry store contract: PASS")
