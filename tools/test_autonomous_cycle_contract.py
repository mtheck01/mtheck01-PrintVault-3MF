from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
workflow = (ROOT / ".github" / "workflows" / "autonomous-cycle.yml").read_text(encoding="utf-8")

required = [
    "concurrency:",
    "workflow_dispatch:",
    "group: printvault-manual-engineering-cycle",
    "cancel-in-progress: false",
    "name: Autonomous Trigger Watchdog",
    "run_cycle=true",
    "run_cycle=false",
    "WATCHDOG_RESULT=UNVALIDATED",
    "WATCHDOG_RESULT=ALREADY_VALIDATED",
    "printvault/whole-library-validation",
    "needs: watchdog",
    "needs.watchdog.outputs.run_cycle == 'true'",
    "curl -fsSL",
    "git fetch origin main --prune",
    "$startingMainSha = (git rev-parse origin/main).Trim()",
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

# The workflow is intentionally manual-only while runner and whole-library
# validation are being stabilized. It must not cancel an active run.
assert "workflow_dispatch:" in workflow
assert "group: printvault-manual-engineering-cycle" in workflow
assert "cancel-in-progress: false" in workflow
assert "  push:" not in workflow
assert "  pull_request:" not in workflow
assert 'cron: "*/15 * * * *"' not in workflow

# Non-dispatch validation must remain zero-credit. OpenAI repair is opt-in only.
assert "AUTO_REPAIR: ${{ github.event_name == 'workflow_dispatch' && inputs.auto_repair || false }}" in workflow
assert "AUTO_REPAIR: ${{ inputs.auto_repair || true }}" not in workflow
assert "PRINTVAULT_ZERO_CREDIT: ${{ github.event_name != 'workflow_dispatch' || inputs.auto_repair == false }}" in workflow
assert "MAX_ATTEMPTS: ${{ github.event_name == 'workflow_dispatch' && inputs.max_attempts || '1' }}" in workflow

assert 'python ".\\tools\\forensic_repository_audit.py"' in workflow
assert "FORENSIC_AUDIT_PASS=$auditPass/2" in workflow
assert "FORENSIC_AUDIT_PRE_INTAKE=PASS" in workflow
assert "MODULE_PASS=$pass/2" in workflow
assert "Passes=2" in workflow
assert "MODULE_LOCKS=CREATED" in workflow
assert "MODULE_LOCK_GATE=PASS" in workflow
assert "Get-SourceFingerprint" in workflow
assert 'git status --porcelain=v1 -z -- . ' in workflow
assert "Every executable test named by the autonomous workflow must physically exist." in (ROOT / "tools" / "forensic_repository_audit.py").read_text(encoding="utf-8")
assert 'SKIP_DIRS = {".git", "bin", "obj"}' in (ROOT / "tools" / "forensic_repository_audit.py").read_text(encoding="utf-8")
assert "Whole-library reproducibility gate failed" in workflow
assert "WHOLE_LIBRARY_TWO_PASS=PASS" in workflow
assert "Invoke-WholeLibrary -Pass 1" in workflow
assert "Invoke-WholeLibrary -Pass 2" in workflow
assert "expected 40, found" in workflow
assert "timeout-minutes: 120" in workflow
assert workflow.index("$startingMainSha = (git rev-parse origin/main).Trim()") < workflow.index("Promotion blocked: origin/main changed during validation.")

assert 'Install-Candidate' in workflow
assert 'Start-FinalApp' in workflow
assert 'Close-PrintVault $finalApp' in workflow
assert 'FINAL_APP_SMOKE_CLOSED=PASS' in workflow
assert 'Cleanup PrintVault process' in workflow
assert 'PRINTVAULT_PROCESS_CLEANUP=PASS' in workflow
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
assert 'WHOLE_LIBRARY_VALIDATION_STATUS=' in workflow
assert '--force-with-lease=refs/heads/${branch}:${expectedBranchSha}' in workflow
assert 'Never use an unleased force push.' in workflow
assert "if ($arg -match '\\s' -or $arg -match '[&|;<>]')" in workflow, "External-process wrapper must quote whitespace/shell-sensitive arguments"
assert 'if ($env:PRINTVAULT_ZERO_CREDIT -eq "1") {' in workflow, "AI repair boundary must hard-block zero-credit execution"

# Candidate build prerequisites must fail closed. A native-process failure cannot
# be allowed to have its exit code overwritten by the next audit command.
assert 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\\tools\\release_preflight.ps1" -Root "$PWD"' in workflow and 'Release preflight failed after candidate source changes' in workflow, "Candidate release preflight exit code is not enforced"
assert 'python ".\\tools\\static_audit.py"' in workflow and 'Static audit failed after candidate source changes' in workflow, "Candidate static-audit exit code is not enforced"
assert 'python ".\\tools\\forensic_repository_audit.py"' in workflow and 'Forensic repository audit failed after candidate source changes' in workflow, "Candidate forensic-audit exit code is not enforced"
