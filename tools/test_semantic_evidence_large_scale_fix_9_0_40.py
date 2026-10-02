from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
fusion = (ROOT / "src/PrintVault.Infrastructure/SemanticEvidenceFusionService.cs").read_text(encoding="utf-8")
rootcause = (ROOT / "src/PrintVault.Infrastructure/WholeLibraryRootCauseAnalysisService.cs").read_text(encoding="utf-8")

checks = [
    ("intelligence score percent calibration", "private static int ClampScore" in fusion and "value * 100.0" in fusion),
    ("expanded creature evidence", '"whale"' in fusion and '"capybara"' in fusion and '"wyvern"' in fusion),
    ("expanded vehicle evidence", '"peterbilt"' in fusion and '"v-22"' in fusion and '"concorde"' in fusion),
    ("expanded prop evidence", '"lightsaber"' in fusion and '"blaster"' in fusion),
    ("expanded functional role evidence", '"phone holder"' in fusion and '"display stand"' in fusion),
    ("expanded art evidence", '"logo"' in fusion and '"watercolor"' in fusion),
    ("multilingual evidence", '"航空母舰"' in fusion and '"支架"' in fusion),
    ("explicit role path is separate", "else if (explicitRole.Hits.Length > 0)" in fusion),
    ("root cause no broad role false positives", '"frame", "base", "support", "display", and "stand"' not in rootcause),
    ("root cause uses precise role phrases", '"business card holder"' in rootcause and '"display stand"' in rootcause),
]

failed = [name for name, ok in checks if not ok]
if failed:
    print("SEMANTIC EVIDENCE 9.0.40 LARGE-SCALE AUDIT FAILED")
    for name in failed:
        print("-", name)
    raise SystemExit(1)

print("SEMANTIC EVIDENCE 9.0.40 LARGE-SCALE AUDIT PASSED")
