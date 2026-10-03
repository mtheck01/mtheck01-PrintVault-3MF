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
    "rootcause $reportDir $expectedCatalog",
    "repair $report $expectedCatalog",
    "VALIDATION=PASS_NO_CONFLICTS",
    "actions/upload-artifact@v4",
]

for item in required:
    assert item in workflow, f"Missing autonomous-chain invariant: {item}"

assert "cancel-in-progress: true" not in workflow
assert workflow.index("$startingMainSha = (git rev-parse origin/main).Trim()") < workflow.index("Promotion blocked: origin/main changed during validation.")

print("PASS: autonomous cycle contract and promotion safety gate")
