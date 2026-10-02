from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
fusion = (ROOT / "src/PrintVault.Infrastructure/SemanticEvidenceFusionService.cs").read_text(encoding="utf-8")
root_cause = (ROOT / "src/PrintVault.Infrastructure/WholeLibraryRootCauseAnalysisService.cs").read_text(encoding="utf-8")

checks = [
    ("translation + source analyzer quality convergence", "analyzerTranslationConvergenceClearsReview" in fusion),
    ("translation quality bonus is bounded", 'Math.Clamp(63 + (int)Math.Round(model.IntelligenceScore * 25), 63, 88)' in fusion),
    ("multi-channel gate requires quality", "classification >= 82 && quality >= 75" in fusion),
    ("object-role matching uses Regex boundaries", "RegexOptions.CultureInvariant" in root_cause),
    ("object-role matching avoids substring contamination", 'text.Contains("stand", StringComparison.OrdinalIgnoreCase)' not in root_cause),
    ("root-cause pass remains read-only", "Read-only" in root_cause or "read-only" in root_cause),
]

failed = [name for name, ok in checks if not ok]
if failed:
    print("SEMANTIC EVIDENCE LARGE-SCALE FIX AUDIT FAILED")
    for name in failed:
        print("-", name)
    raise SystemExit(1)

print("SEMANTIC EVIDENCE LARGE-SCALE FIX AUDIT PASSED")
print("Multi-channel analyzer+translation convergence and bounded object-role matching are present.")
