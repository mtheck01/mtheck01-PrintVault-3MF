from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
VERSION = (ROOT / "VERSION").read_text(encoding="utf-8").strip()
errors = []

required = [
    ROOT / "src/PrintVault/App.xaml.cs",
    ROOT / "src/PrintVault/MainWindow.xaml.cs",
    ROOT / "src/PrintVault/MainWindow.xaml",
    ROOT / "src/PrintVault.Infrastructure/LibraryRepository.cs",
    ROOT / "src/PrintVault.Infrastructure/LibraryEngine.cs",
    ROOT / "src/PrintVault.Infrastructure/OrganizationService.cs",
    ROOT / "src/PrintVault.Infrastructure/ThumbnailService.cs",
    ROOT / "src/PrintVault.Infrastructure/LibraryConsolidationService.cs",
    ROOT / "src/PrintVault.Infrastructure/CategoryReconciliationService.cs",
    ROOT / "src/PrintVault/ConsolidationWindow.cs",
    ROOT / "src/PrintVault.Core/Commands.cs",
]
for p in required:
    if not p.exists(): errors.append(f"missing: {p.relative_to(ROOT)}")

project = (ROOT / "src/PrintVault/PrintVault.csproj").read_text()
iss = (ROOT / "installer/PrintVault_3MF.iss").read_text()
build = (ROOT / "build_windows.bat").read_text()
commands = (ROOT / "src/PrintVault.Core/Commands.cs").read_text()
repo = (ROOT / "src/PrintVault.Infrastructure/LibraryRepository.cs").read_text()
xaml = (ROOT / "src/PrintVault/MainWindow.xaml").read_text()
code = (ROOT / "src/PrintVault/MainWindow.xaml.cs").read_text()

checks = [
    (f'<Version>{VERSION}</Version>' in project or (ROOT / "Directory.Build.props").exists(), "project version source missing"),
    ('AppVersion={#MyAppVersion}' in iss, "installer template must consume MyAppVersion"),
    ('/DMyAppVersion=%VERSION%' in build, "build script must pass MyAppVersion to Inno Setup"),
    ("GetName().Version" in commands, "runtime version must derive from assembly"),
    ("PRAGMA table_info(Models)" in repo, "schema migration check missing"),
    ("ALTER TABLE Models ADD COLUMN" in repo, "schema ALTER migration missing"),
    ("IX_Models_Hash" in repo, "hash index missing"),
    ("SELECT * FROM Models" not in repo, "repository contains unsafe SELECT *"),
    ('SelectionChanged="Models_SelectionChanged"' in xaml, "selection changed event missing"),
    ("void Models_SelectionChanged" in code, "selection changed handler missing"),
    ("PreviousCategory" in (ROOT / "src/PrintVault.Infrastructure/OrganizationService.cs").read_text(), "undo metadata contract missing"),
    ("ReparsePoint" in (ROOT / "src/PrintVault.Infrastructure/LibraryFileDiscovery.cs").read_text(), "reparse-point protection missing from discovery module"),
    ("LibraryConsolidationService" in code and "UndoConsolidation_Click" in code, "smart consolidation integration missing"),
    ("CategoryReconciliationService" in code and "ReconcileCategories_Click" in code, "category reconciliation integration missing"),
    ("UndoCategoryReconciliation_Click" in code, "category reconciliation undo integration missing"),
    ("last_category_reconciliation.json" in (ROOT / "src/PrintVault.Infrastructure/CategoryReconciliationService.cs").read_text(), "category reconciliation undo missing"),
    ("last_consolidation.json" in (ROOT / "src/PrintVault.Infrastructure/LibraryConsolidationService.cs").read_text(), "persistent consolidation undo missing"),
    ("PreviousCategoryOverride" in (ROOT / "src/PrintVault.Infrastructure/LibraryConsolidationService.cs").read_text(), "consolidation override state not persisted"),
    ("GetCategoryDirectory" in (ROOT / "src/PrintVault.Infrastructure/LibraryConsolidationService.cs").read_text(), "consolidation root safety missing"),
    ('x:Name="CategoryFilter"' in xaml and 'BrowseFilter_Changed' in xaml, "category browse filter missing"),
    ('x:Name="TagFilter"' in xaml and 'BrowseFilter_Changed' in xaml, "tag browse filter missing"),
    ('GridViewColumnHeader.Click="GridViewColumnHeader_Click"' in xaml, "column sorting integration missing"),
    ('CategoryStat_Click' in xaml and 'categoryFilter = name' in code, "dashboard category navigation missing"),
]

# Challenge reports must never drift behind the executable version. They must
# derive report BUILD/title metadata from AppVersion.Version.
challenge_services = sorted((ROOT / 'src/PrintVault.Infrastructure').glob('*ChallengeService.cs'))
for p in challenge_services:
    s = p.read_text(encoding='utf-8', errors='ignore')
    if 'AppVersion.Version' not in s:
        errors.append(f'challenge service version source missing: {p.relative_to(ROOT)}')
    if re.search(r'BUILD:\s*\d+\.\d+\.\d+', s):
        errors.append(f'hard-coded BUILD version in: {p.relative_to(ROOT)}')
    if re.search(r'PrintVault 3MF\s+\d+\.\d+\.\d+', s):
        errors.append(f'hard-coded report title version in: {p.relative_to(ROOT)}')

for ok, msg in checks:
    if not ok: errors.append(msg)

# Basic delimiter sanity for C# source.
for p in ROOT.glob("src/**/*.cs"):
    s = p.read_text(errors="ignore")
    if s.count("{") != s.count("}"): errors.append(f"brace mismatch: {p.relative_to(ROOT)}")
    if s.count("(") != s.count(")"): errors.append(f"paren mismatch: {p.relative_to(ROOT)}")

handlers = set(re.findall(r'(?:Click|Loaded|PreviewKeyDown|SelectionChanged|MouseDoubleClick|PreviewMouseRightButtonDown|MouseLeftButtonDown)="([A-Za-z_][A-Za-z0-9_]*)"', xaml))
methods = set(re.findall(r'\b(?:private|public|protected|internal)\s+(?:async\s+)?(?:void|Task(?:<[^>]+>)?|[A-Za-z_][\w<>?]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(', code))
for h in sorted(handlers - methods): errors.append(f"XAML handler missing: {h}")

if errors:
    print("STATIC AUDIT FAILED")
    for e in errors: print("-", e)
    raise SystemExit(1)
print(f"STATIC AUDIT PASSED — PrintVault {VERSION}")

main = ROOT / "src" / "PrintVault" / "MainWindow.xaml"
code = ROOT / "src" / "PrintVault" / "MainWindow.xaml.cs"
org = ROOT / "src" / "PrintVault.Infrastructure" / "OrganizationService.cs"
xaml = main.read_text(encoding="utf-8")
cs = code.read_text(encoding="utf-8")
orgcs = org.read_text(encoding="utf-8")
checks = {
    "tags_column": 'Header="Tags"' in xaml,
    "tags_inspector": 'InspectorTags' in xaml and 'TagsWindow' in cs,
    "tags_context_action": 'EditTags_Click' in cs and 'GalleryEditTags_Click' in cs,
    "category_manager": 'CategoryManagerWindow' in cs,
    "custom_category_persistence": 'SaveCategories()' in cs and 'categories.txt' in cs,
    "db_categories_restored": 'engine.Repository.GetAll().Select(x => x.Category)' in cs,
    "category_tag_preserved": 'ReplaceCategoryTag' in orgcs,
    "scan_defer_refresh_not_used": 'ModelView.DeferRefresh()' not in cs,
}
failed = [k for k,v in checks.items() if not v]
if failed:
    raise SystemExit("UI AUDIT FAILED: " + ", ".join(failed))
print("UI FEATURE AUDIT PASSED — " + str(len(checks)) + " checks")
