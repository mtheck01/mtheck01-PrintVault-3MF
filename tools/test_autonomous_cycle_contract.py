from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
workflow = (ROOT / ".github" / "workflows" / "autonomous-cycle.yml").read_text(encoding="utf-8")

required = [
    "concurrency:",
    "cancel-in-progress: false",
    "git fetch origin main --prune",
    "$startingMainSha = (git rev-parse origin/main).Trim()",
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
assert workflow.index("$startingMainSha = (git rev-parse origin/main).Trim()") < workflow.index("Promotion blocked: origin/main changed during validation.")

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
