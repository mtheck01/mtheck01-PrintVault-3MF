from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
service = (ROOT / "src/PrintVault.Infrastructure/WholeLibraryRootCauseAnalysisService.cs").read_text(encoding="utf-8")
program = (ROOT / "tools/PrintVault.Automation/Program.cs").read_text(encoding="utf-8")
workflow = (ROOT / ".github/workflows/autonomous-cycle.yml").read_text(encoding="utf-8")
analyzer = (ROOT / "src/PrintVault.Infrastructure/ThreeMfAnalyzer.cs").read_text(encoding="utf-8")
rootcause = service

checks = {
    "records analyzed is distinct from conflict review rows": "RECORDS_ANALYZED=" in program and "CONFLICT_REVIEW_ROWS=" in program,
    "all successful analyses emit a per-record outcome": "outcomes.Add(new RootCauseOutcome(" in service,
    "forensic analysis re-runs source-derived 3MF analysis": (
        "sourceAnalyzer.AnalyzeForensicFastGeometry(source.Path, source.Name, out var analysisTrace)" in service
        and re.search(r"AnalyzeSourceForForensics\(m,\s*(?:worker\.SourceAnalyzer,\s*)?out var sourceTrace\)", service) is not None
    ),
    "forensic evidence includes bounded source, fusion, arbitration, and timing trace": (
        "evidenceTraceSchemaVersion = 3" in service
        and "evidenceTraces = orderedEvidenceTraces" in service
        and "stage.StageTrace" in service
        and "f.Diagnostics" in service
        and "SemanticInputsTruncated" in analyzer
        and "sourceAnalyzer.AnalyzeForensicFastGeometry(source.Path, source.Name, out var analysisTrace)" in service
        and "Concat(stage.StageEvidence)" in service
    ),
    "forensic source analysis retains semantic traces and uses bounded geometry probing": (
        "AnalyzeForensicFastGeometry(string path, string name, out ThreeMfAnalysisTrace trace)" in analyzer
        and "AnalyzeCore(path, name, true, false, out trace)" in analyzer
        and "AnalyzeCore(path, name, true, true, out trace)" in analyzer
        and "stopAfterFirstReachableGeometry: !exactComponentGeometry" in analyzer
        and "trace.ComponentGeometryCountsExact = geometry.CountsExact" in analyzer
        and "sourceAnalyzer.AnalyzeForensicFastGeometry(source.Path, source.Name, out var analysisTrace)" in service
    ),
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
    "forensic source analyzer category is forwarded through arbitration": (
        "SOURCE_ANALYZER_CATEGORY_CANDIDATE:" in service
        and "model.Category = analysis.Category" not in service
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
    "candidate and applied category distributions are exported": "appliedCategories" in service and "candidateCategories" in service,
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
    "model materials are collected during the same XML traversal": "materials.Add(nameAttribute)" in analyzer and "var parsedModel = ReadModelXml" in analyzer and "foreach(var b in modelDocument.Descendants()" not in analyzer,
    "large geometry avoids repeated container-value expansion": "if (!element.HasElements) allText.Append(' ').Append(element.Value);" in analyzer and "!localName.Equals(\"vertex\", StringComparison.OrdinalIgnoreCase)" in analyzer and "!localName.Equals(\"triangle\", StringComparison.OrdinalIgnoreCase)" in analyzer,
    "large model XML uses the streaming reader above a bounded threshold": "StreamingModelXmlThresholdBytes = 32L * 1024 * 1024" in analyzer and "XmlReader.Create(stream, new XmlReaderSettings" in analyzer and "model.Length >= StreamingModelXmlThresholdBytes" in analyzer,
    "streaming parser preserves traceable XML size and parser choice": "MainModelXmlUncompressedBytes" in analyzer and "StreamedMainModelXml" in analyzer,
    "root-cause logs expose per-file parser phase timings": "xml_ms={analysisTrace.MainModelXmlLoadMilliseconds}" in rootcause and "geometry_ms={analysisTrace.GeometryResolutionMilliseconds}" in rootcause and "xml_uncompressed_bytes={analysisTrace.MainModelXmlUncompressedBytes}" in rootcause,
}
failed = [name for name, passed in checks.items() if not passed]
for name, passed in checks.items():
    print(("PASS: " if passed else "FAIL: ") + name)
if failed:
    raise SystemExit("WHOLE-LIBRARY DIAGNOSTIC ACCOUNTING FAILED: " + ", ".join(failed))
print(f"WHOLE-LIBRARY DIAGNOSTIC ACCOUNTING PASSED — {len(checks)} checks")

