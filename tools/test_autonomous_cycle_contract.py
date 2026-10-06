from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
workflow = (ROOT / ".github" / "workflows" / "autonomous-cycle.yml").read_text(encoding="utf-8")

required = [
    "concurrency:",
    "cancel-in-progress: false",
    "git fetch origin main --prune",
    "$startingMainSha = (git rev-parse origin/main).Trim()",
    'if ($env:GITHUB_EVENT_NAME -eq "schedule") {',
    'https://api.github.com/repos/$env:GITHUB_REPOSITORY/releases/latest',
    'if ($latestReleaseSha -eq $startingMainSha) {',
    'SCHEDULE_VALIDATION_NEEDED=NO',
    'scheduled poll: main already matches latest published release',
    'Refusing to manufacture a release',
    "PROMOTION_GUARD_START_SHA=",
    "Promotion blocked: origin/main changed during validation.",
    "Invoke-WholeLibrary",
    "VALIDATION=PASS_NO_CONFLICTS",
    "actions/upload-artifact@v4",
]

for item in required:
    assert item in workflow, f"Missing autonomous-chain invariant: {item}"

# Accept either PowerShell's quoted argument form or the historical unquoted form.
# The contract should validate the command semantics, not a fragile exact substring.
assert "rootcause" in workflow and "$reportDir" in workflow and "$expectedCatalog" in workflow, \
    "Missing autonomous whole-library rootcause invocation"
assert "repair $report $expectedCatalog" in workflow or \
       'repair $report $expectedCatalog' in workflow, \
    "Missing autonomous repair invocation"

assert "cancel-in-progress: true" not in workflow

assert "MODULE_PASS=$pass/2" in workflow
assert "Passes=2" in workflow
assert "MODULE_LOCKS=CREATED" in workflow
assert "MODULE_LOCK_GATE=PASS" in workflow
assert "Get-SourceFingerprint" in workflow
assert "Whole-library reproducibility gate failed" in workflow
assert "WHOLE_LIBRARY_TWO_PASS=PASS" in workflow
assert "Invoke-WholeLibrary -Pass 1" in workflow
assert "Invoke-WholeLibrary -Pass 2" in workflow
assert "expected 40, found" in workflow
assert "timeout-minutes: 120" in workflow
assert workflow.index("$startingMainSha = (git rev-parse origin/main).Trim()") < workflow.index("Promotion blocked: origin/main changed during validation.")

assert 'Install-Candidate' in workflow
assert 'Start-FinalApp' in workflow
assert 'APPLICATION=DEFERRED_UNTIL_VALIDATION_PASS' in workflow
assert workflow.index('LEGACY_TAXONOMY_CLEANUP') < workflow.index('FINAL_APP_SMOKE')
assert 'LEGACY_REGISTRY_ENTRIES_REMAINING=0' in workflow
assert 'LEGACY_REGISTRY_ENTRIES_REMAINING=' in (ROOT / "tools" / "PrintVault.Automation" / "Program.cs").read_text(encoding="utf-8")
assert 'CleanupLegacyTaxonomy' in (ROOT / "src" / "PrintVault.Infrastructure" / "OrganizationService.cs").read_text(encoding="utf-8")
assert 'if (IsCustomCategory(top) && !IsLegacyCategory(top)) return top;' in (ROOT / "src" / "PrintVault.Infrastructure" / "LibraryEngine.cs").read_text(encoding="utf-8")
assert '!IsLegacyCategory(x)' in (ROOT / "src" / "PrintVault" / "MainWindow.xaml.cs").read_text(encoding="utf-8")

assert 'INITIAL_VERSION_BUMP=PASS' in workflow
assert workflow.index('INITIAL_VERSION_BUMP=PASS') < workflow.index('$candidate = Invoke-CandidateBuild')
assert 'PRINTVAULT_AUTONOMOUS_CLASSIFICATION_ONLY = "1"' in workflow
assert 'autonomousClassificationOnly' in (ROOT / "src" / "PrintVault.Infrastructure" / "LibraryEngine.cs").read_text(encoding="utf-8")
assert 'doHash = !autonomousClassificationOnly' in (ROOT / "src" / "PrintVault.Infrastructure" / "LibraryEngine.cs").read_text(encoding="utf-8")
assert 'doThumbnail = !autonomousClassificationOnly' in (ROOT / "src" / "PrintVault.Infrastructure" / "LibraryEngine.cs").read_text(encoding="utf-8")
print("PASS: autonomous cycle contract and promotion safety gate")

# The validation runner must use only the isolated test library and must never
# fall back to the production D: library or an arbitrary environment path.
assert r'D:\3d print files test' in workflow, "Missing isolated test-library root"
assert r'D:\3d print files' in workflow, "Missing production-library safety guard"
assert 'Refusing to scan unapproved test-library path' in workflow
assert 'PRODUCTION_LIBRARY_SCAN=DISABLED' in workflow
assert 'DRIVE_ROOT_SCAN=DISABLED' in workflow


# Spaced test-library paths must not cross the autonomous process boundary as
# positional command-line arguments. The scanner receives the exact path via
# PRINTVAULT_TEST_LIBRARY_ROOT instead.
assert '$env:PRINTVAULT_TEST_LIBRARY_ROOT = $testLibraryRoot' in workflow,     "Autonomous cycle must export the isolated test root"
assert 'Environment.GetEnvironmentVariable("PRINTVAULT_TEST_LIBRARY_ROOT")' in     (ROOT / "tools" / "PrintVault.Automation" / "Program.cs").read_text(encoding="utf-8"),     "Automation scanner must consume PRINTVAULT_TEST_LIBRARY_ROOT"
assert 'scan", "", "Deep"' in workflow,     "Autonomous scan must not pass the spaced test root as a positional argument"
assert 'cleanup", "")' in workflow,     "Autonomous cleanup must not pass the spaced test root as a positional argument"
assert 'LEGACY_CATEGORIES_REMAINING=0' in workflow
assert 'LEGACY_TAGS_REMAINING=0' in workflow
assert 'LEGACY_FOLDERS_REMAINING=0' in workflow
assert 'FILES_BEFORE=' in workflow and 'FILES_AFTER=' in workflow
assert 'Legacy taxonomy cleanup failed' in workflow
assert 'Test-library organization failed' not in workflow

current_modular_tests = [
    "test_8_6_library_lifecycle.py", "test_browse_sorting.py",
    "test_category_dropdown_fix3.py", "test_category_navigation_fix2.py",
    "test_category_reconciliation.py", "test_change_category_readability.py",
    "test_clear_scan_race.py", "test_consolidation_deep.py",
    "test_full_library_intelligence_9_0_11.py", "test_intelligence_8_4.py",
    "test_language_intelligence_module.py", "test_library_intelligence_health.py",
    "test_scan_stability.py", "test_schema_migration.py",
    "test_semantic_evidence_fusion_9_0_13.py", "test_semantic_evidence_taxonomy_contract_9_0_24.py",
    "test_semantic_intelligence.py", "test_semantic_relationship_integration_9_0_20.py",
    "test_smart_category_engine.py", "test_smart_category_review.py",
    "test_whole_library_entity_coverage_v1.py",
    "test_autonomous_engineering_intake.py"
]
for test_name in current_modular_tests:
    assert test_name in workflow, f"Current modular regression test is not wired into autonomous cycle: {test_name}"

assert 'Refusing to manufacture a release' in workflow
assert 'SCHEDULE_VALIDATION_NEEDED=NO' in workflow
assert 'SCHEDULE_VALIDATION_NEEDED=YES' in workflow
assert 'git switch -c $branch' in workflow
assert 'git fetch origin "+refs/heads/$branch:refs/remotes/origin/$branch"' in workflow
assert '--force-with-lease=refs/heads/${branch}:${expectedBranchSha}' in workflow
assert 'Never use an unleased force push.' in workflow
assert workflow.index('$startingMainSha = (git rev-parse origin/main).Trim()') < workflow.index('git switch -c $branch')
