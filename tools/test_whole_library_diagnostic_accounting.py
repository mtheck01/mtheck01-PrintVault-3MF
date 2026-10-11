from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
service = (ROOT / "src/PrintVault.Infrastructure/WholeLibraryRootCauseAnalysisService.cs").read_text(encoding="utf-8")
program = (ROOT / "tools/PrintVault.Automation/Program.cs").read_text(encoding="utf-8")
workflow = (ROOT / ".github/workflows/autonomous-cycle.yml").read_text(encoding="utf-8")

checks = {
    "records analyzed is distinct from conflict review rows": "RECORDS_ANALYZED=" in program and "CONFLICT_REVIEW_ROWS=" in program,
    "all successful analyses emit a per-record outcome": "outcomes.Add(new RootCauseOutcome(" in service,
    "full outcome CSV is written": "BuildOutcomesCsv(outcomes)" in service and "OUTCOMES_CSV=" in program,
    "resolved and unresolved outcomes are counted": "resolvedInferences" in service and "unresolvedInferences" in service,
    "missing inference is tracked": "missingInferenceResults" in service and "MISSING_INFERENCE" in service,
    "category distribution is exported": "inferredCategories" in service,
    "outcome fingerprint is deterministic": "SHA256.HashData" in service and "OutcomeFingerprint" in service,
    "processed count excludes failed records": "created, models.Count, recordsAnalyzed, conflicts" in service,
    "automation enforces inference accounting": "DIAGNOSTIC_ACCOUNTING=FAIL" in program,
    "automation rejects empty evidence coverage": "DIAGNOSTIC_EVIDENCE_COVERAGE=FAIL" in program,
    "evidence-bearing results require independent semantic evidence": "hasIndependentSemanticEvidence" in service and "!IsUnresolvedCategory(inferred)" in service,
    "automation fails unresolved rates above 50 percent": "DIAGNOSTIC_UNRESOLVED_RATE=FAIL MAX=50%" in program,
    "workflow rejects unresolved rates above 50 percent": "REGRESSION_GATE=FAIL_REASON=UNRESOLVED_RATE MAX=50%" in workflow,
    "two-pass gate compares per-record fingerprint": "$reportData.OutcomeFingerprint -ne $reportData2.OutcomeFingerprint" in workflow,
    "regression gate validates record accounting": "REGRESSION_GATE=FAIL_REASON=INFERENCE_ACCOUNTING" in workflow,
    "zero conflicts alone cannot bypass coverage": "REGRESSION_GATE=FAIL_REASON=NON_INFORMATIVE_ANALYSIS" in workflow,
    "outcome CSV is uploaded as a diagnostic artifact": 'Filter "whole-library-outcomes.*.csv"' in workflow,
}
failed = [name for name, passed in checks.items() if not passed]
for name, passed in checks.items():
    print(("PASS: " if passed else "FAIL: ") + name)
if failed:
    raise SystemExit("WHOLE-LIBRARY DIAGNOSTIC ACCOUNTING FAILED: " + ", ".join(failed))
print(f"WHOLE-LIBRARY DIAGNOSTIC ACCOUNTING PASSED — {len(checks)} checks")
