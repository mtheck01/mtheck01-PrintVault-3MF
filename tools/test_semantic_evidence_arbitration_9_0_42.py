from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
fusion = (ROOT / "src/PrintVault.Infrastructure/SemanticEvidenceFusionService.cs").read_text(encoding="utf-8")

checks = [
    ("strong analyzer arbitration exists", "else if (strongSourceDerivedAnalyzer)" in fusion),
    ("analyzer arbitration uses analyzer category", 'category = analyzerCategory;' in fusion),
    ("lexical evidence is retained without promotion", "Lexical cue retained as non-promoting evidence" in fusion),
    ("same-domain lexical corroboration is explicit", "Source-derived analyzer + lexical corroboration" in fusion),
    ("aviation convergence cannot override strong analyzer", "if (aviationConvergence && entity is null && !strongSourceDerivedAnalyzer)" in fusion),
    ("final aviation gate cannot override strong analyzer", "if (aviationConvergence && !strongSourceDerivedAnalyzer)" in fusion),
    ("analyzer family has deterministic fallback", "InferFamily(analyzerCategory)" in fusion),
    ("confidence calibration remains present", "value * 100.0" in fusion),
]

failed = [name for name, ok in checks if not ok]
if failed:
    print("SEMANTIC EVIDENCE 9.0.42 ARBITRATION AUDIT FAILED")
    for name in failed:
        print("-", name)
    raise SystemExit(1)

# Guard the intended precedence ordering: strong analyzer arbitration must occur
# before generic lexical promotion in the Fuse decision chain.
analyzer_pos = fusion.index("else if (strongSourceDerivedAnalyzer)")
lexical_pos = fusion.index("else if (cueHits.Count > 0 && best.Hits.Length > 0)")
if analyzer_pos >= lexical_pos:
    raise SystemExit("SEMANTIC EVIDENCE 9.0.42 ARBITRATION AUDIT FAILED: lexical promotion precedes analyzer arbitration")

print("SEMANTIC EVIDENCE 9.0.42 ARBITRATION AUDIT PASSED")
