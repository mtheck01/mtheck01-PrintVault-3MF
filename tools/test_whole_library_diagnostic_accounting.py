from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
service = (ROOT / "src/PrintVault.Infrastructure/WholeLibraryRootCauseAnalysisService.cs").read_text(encoding="utf-8")
program = (ROOT / "tools/PrintVault.Automation/Program.cs").read_text(encoding="utf-8")
workflow = (ROOT / ".github/workflows/autonomous-cycle.yml").read_text(encoding="utf-8")
analyzer = (ROOT / "src/PrintVault.Infrastructure/ThreeMfAnalyzer.cs").read_text(encoding="utf-8")

checks = {
    "records analyzed is distinct from conflict review rows": "RECORDS_ANALYZED=" in program and "CONFLICT_REVIEW_ROWS=" in program,
    "all successful analyses emit a per-record outcome": "outcomes.Add(new RootCauseOutcome(" in service,
    "forensic analysis re-runs source-derived 3MF analysis": (
        "sourceAnalyzer.Analyze(source.Path, source.Name)" in service
        and re.search(r"AnalyzeSourceForForensics\(m,\s*(?:worker\.SourceAnalyzer,\s*)?out var sourceTrace\)", service) is not None
    ),
    "forensic evidence includes per-stage trace": "Concat(stage.StageEvidence)" in service and "SOURCE_ANALYZER:COMPLETED" in service,
    "source-analysis errors are explicit instead of silent": "SOURCE_ANALYZER:FAILED:" in service,
    "forensic source enrichment uses an isolated record copy": "CloneForForensics(source)" in service and "private static ModelRecord CloneForForensics" in service,
    "expensive source parsing is gated by unresolved or incomplete semantic metadata": (
        "SOURCE_ANALYZER:SKIPPED_EXISTING_SEMANTIC_METADATA" in service
        and "if (!unresolvedCategory && !missingSemanticMetadata)" in service
        and re.search(r"var missingSemanticMetadata\s*=([^;]+);", service, re.DOTALL) is not None
        and "source.IntelligenceReason" not in re.search(r"var missingSemanticMetadata\s*=([^;]+);", service, re.DOTALL).group(1)
    ),
    "forensic pass measures source-preparation cost": (
        "Stopwatch.GetTimestamp()" in service
        and "Stopwatch.GetElapsedTime(sourceStarted)" in service
        and "SOURCE_FORENSICS_ELAPSED_MS=" in service
    ),
    "forensic source analysis cannot overwrite a resolved category": (
        "var mayReplaceCategory = !source.CategoryOverride && currentIsUnresolved;" in service
        and "analysis.Confidence >= 0.70" in service
    ),
    "unresolved records are included in root-cause rows": (
        "rootCauseRows.Add(row)" in service
        and re.search(
            r"if\s*\([^)]*(?:inferenceMissing|unresolved|IsUnresolvedCategory\(inferred\))[^)]*\)\s*\{?\s*(?:var cause|var disposition|var row)",
            service,
            re.DOTALL,
        ) is not None
    ),
    "root-cause summaries aggregate unresolved records": (
        re.search(r"(?:orderedRootCauseRows|rootCauseRows)\.GroupBy\(x => x\.Cause\)", service) is not None
        and re.search(r"(?:orderedRootCauseRows|rootCauseRows)\.GroupBy\(x => x\.Disposition\)", service) is not None
    ),
    "unresolved outcomes get an actionable diagnostic disposition": 'return "UNRESOLVED_ENGINE_GAP";' in service,
    "report separately preserves conflict-review counts": "conflictReviewRowCount" in service and "rootCauseRecords = rootCauseRows.Count" in service,
    "full outcome CSV is written": (
        re.search(r"File\.WriteAllText\(outcomesCsv,\s*BuildOutcomesCsv\((?:orderedOutcomes|outcomes)\)", service) is not None
        and "OUTCOMES_CSV=" in program
    ),
    "resolved and unresolved outcomes are counted": "resolvedInferences" in service and "unresolvedInferences" in service,
    "missing inference is tracked": "missingInferenceResults" in service and "MISSING_INFERENCE" in service,
    "category distribution is exported": "inferredCategories" in service,
    "outcome fingerprint is deterministic": "SHA256.HashData" in service and "OutcomeFingerprint" in service,
    "processed count excludes failed records": "created, models.Count, recordsAnalyzed, conflicts" in service,
    "automation enforces inference accounting": "DIAGNOSTIC_ACCOUNTING=FAIL" in program,
    "automation rejects empty evidence coverage": "DIAGNOSTIC_EVIDENCE_COVERAGE=FAIL" in program,
    "two-pass gate compares per-record fingerprint": "$reportData.OutcomeFingerprint -ne $reportData2.OutcomeFingerprint" in workflow,
    "regression gate validates record accounting": "REGRESSION_GATE=FAIL_REASON=INFERENCE_ACCOUNTING" in workflow,
    "regression gate prioritizes a clean baseline-or-better result over improvement labeling": (
        workflow.find("if ($r.Conflicts -le $baseline.conflicts)") >= 0
        and workflow.find("if ($r.Conflicts -le $baseline.conflicts)") < workflow.find("return \"IMPROVEMENT\"")
    ),
    "zero conflicts alone cannot bypass coverage": "REGRESSION_GATE=FAIL_REASON=NON_INFORMATIVE_ANALYSIS" in workflow,
    "outcome CSV is uploaded as a diagnostic artifact": 'Filter "whole-library-outcomes.*.csv"' in workflow,
    "repeated semantic phrase scoring reuses compiled regex matchers": "TokenRegexCache.GetOrAdd" in analyzer and "RegexOptions.Compiled | RegexOptions.CultureInvariant" in analyzer,
    "compact semantic text is normalized once per model": "compactSemanticScoringText = CompactForTokenMatching(semanticScoringText)" in analyzer,
    "basematerial extraction reuses parsed model XML": "XDocument? modelDocument = null" in analyzer and "if(modelDocument!=null){foreach(var b in modelDocument.Descendants()" in analyzer,
}
failed = [name for name, passed in checks.items() if not passed]
for name, passed in checks.items():
    print(("PASS: " if passed else "FAIL: ") + name)
if failed:
    raise SystemExit("WHOLE-LIBRARY DIAGNOSTIC ACCOUNTING FAILED: " + ", ".join(failed))
print(f"WHOLE-LIBRARY DIAGNOSTIC ACCOUNTING PASSED — {len(checks)} checks")

