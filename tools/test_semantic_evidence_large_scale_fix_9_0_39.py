from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
fusion = (ROOT / "src/PrintVault.Infrastructure/SemanticEvidenceFusionService.cs").read_text(encoding="utf-8")
analyzer = (ROOT / "src/PrintVault.Infrastructure/ThreeMfAnalyzer.cs").read_text(encoding="utf-8")

checks = [
    ("strong source analyzer calibration", "strongSourceDerivedAnalyzer" in fusion and "quality = Math.Max(quality, 78)" in fusion),
    ("broad creature coverage", '"articulated dragon"' in fusion),
    ("broad decor coverage", '"lithophane"' in fusion),
    ("household coverage", '"soap dispenser"' in fusion),
    ("separator normalization", "NormalizeSemanticSeparators" in analyzer),
    ("designation normalization", "compactPhrase.Length >= 3" in analyzer),
    ("aircraft designation coverage", '"ah-64"' in analyzer and '"bf-109"' in analyzer),
]

failed = [name for name, ok in checks if not ok]
if failed:
    print("SEMANTIC EVIDENCE 9.0.39 LARGE-SCALE AUDIT FAILED")
    for name in failed:
        print("-", name)
    raise SystemExit(1)

print("SEMANTIC EVIDENCE 9.0.39 LARGE-SCALE AUDIT PASSED")
