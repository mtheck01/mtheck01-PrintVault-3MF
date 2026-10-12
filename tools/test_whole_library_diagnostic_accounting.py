from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
service = (ROOT / "src/PrintVault.Infrastructure/WholeLibraryRootCauseAnalysisService.cs").read_text(encoding="utf-8")
program = (ROOT / "tools/PrintVault.Automation/Program.cs").read_text(encoding="utf-8")
workflow = (ROOT / ".github/workflows/autonomous-cycle.yml").read_text(encoding="utf-8")

checks = {
    "records analyzed is distinct from conflict review rows": "RECORDS_ANALYZED=" in program and "CONFLICT_REVIEW_ROWS=" in program,
    "all successful analyses emit a per-record outcome": "outcomes.Add(new RootCauseOutcome(" in service,
    "forensic analysis re-runs source-derived 3MF analysis": "sourceAnalyzer.Analyze(source.Path, source.Name)" in service and "AnalyzeSourceForForensics(m, out var sourceTrace)" in service,
    "forensic evidence includes per-stage trace": "Concat(stage.StageEvidence)" in service and "SOURCE_ANALYZER:COMPLETED" in service,
    "source-analysis errors are explicit instead of silent": "SOURCE_ANALYZER:FAILED:" in service,
    "forensic source enrichment uses an isolated record copy": "CloneForForensics(source)" in service and "private static ModelRecord CloneForForensics" in service,
    "unresolved records are included in root-cause rows": "inferenceMissing || IsUnresolvedCategory(inferred)" in service and "rootCauseRows.Add(row)" in service,
    "root-cause summaries aggregate unresolved records": "rootCauseRows.GroupBy(x => x.Cause)" in service and "rootCauseRows.GroupBy(x => x.Disposition)" in service,
    "unresolved outcomes get an actionable diagnostic disposition": 'return "UNRESOLVED_ENGINE_GAP";' in service,
    "report separately preserves conflict-review counts": "conflictReviewRowCount" in service and "rootCauseRecords = rootCauseRows.Count" in service,
    "full outcome CSV is written": "BuildOutcomesCsv(outcomes)" in service and "OUTCOMES_CSV=" in program,
    "resolved and unresolved outcomes are counted": "resolvedInferences" in service and "unresolvedInferences" in service,
    "missing inference is tracked": "missingInferenceResults" in service and "MISSING_INFERENCE" in service,
    "category distribution is exported": "inferredCategories" in service,
    "outcome fingerprint is deterministic": "SHA256.HashData" in service and "OutcomeFingerprint" in service,
    "processed count excludes failed records": "created, models.Count, recordsAnalyzed, conflicts" in service,
    "automation enforces inference accounting": "DIAGNOSTIC_ACCOUNTING=FAIL" in program,
    "automation rejects empty evidence coverage": "DIAGNOSTIC_EVIDENCE_COVERAGE=FAIL" in program,
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
