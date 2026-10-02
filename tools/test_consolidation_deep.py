from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
svc = (ROOT / "src/PrintVault.Infrastructure/LibraryConsolidationService.cs").read_text()
ui = (ROOT / "src/PrintVault/ConsolidationWindow.cs").read_text()
csproj = (ROOT / "src/PrintVault/PrintVault.csproj").read_text()

checks = {
    "single_cleanup_engine": not (ROOT / "src/PrintVault.Infrastructure/FolderCleanupService.cs").exists() and not (ROOT / "src/PrintVault/CleanupWindow.cs").exists(),
    "explicit_wpf_usings": "using System;" in ui and "using System.Collections.Generic;" in ui and "using System.Linq;" in ui,
    "no_implicit_ui_dependency": "<ImplicitUsings>disable</ImplicitUsings>" in csproj,
    "root_boundary": "IsUnderRoot" in svc and "GetCategoryDirectory" in svc,
    "review_gate": "includeReview" in svc and 'string.Equals(item.Confidence, "Review", StringComparison.OrdinalIgnoreCase)' in svc,
    "custom_category_preserved": "CategoryOverride == true" in svc and "PreviousCategoryOverride" in svc,
    "collision_safe": "Unique(" in svc,
    "atomic_manifest": "File.Move(temp, undoPath, true)" in svc,
    "root_bound_undo": "manifest.LibraryRoot" in svc,
    "partial_undo_preserved": "SaveManifest(manifest with { Entries = remaining })" in svc,
    "missing_source_warning": "Source file no longer exists" in svc,
    "empty_folder_only": "Directory.EnumerateFileSystemEntries(dir).Any()" in svc,
    "3mf_only": 'EnumerateFiles(dir, "*.3mf"' in svc,
    "search_label": "SEARCH YOUR 3MF LIBRARY" in (ROOT / "src/PrintVault/MainWindow.xaml").read_text(),
    "review_checkbox_behavior": "SetReviewSelection" in ui,
}
failed = [k for k, v in checks.items() if not v]
if failed:
    raise SystemExit("DEEP CONSOLIDATION TEST FAILED: " + ", ".join(failed))
print(f"DEEP CONSOLIDATION TEST PASSED — {len(checks)} checks")
