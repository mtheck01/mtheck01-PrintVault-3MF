from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
fusion = (ROOT / "src/PrintVault.Infrastructure/SemanticEvidenceFusionService.cs").read_text(encoding="utf-8")
entity = (ROOT / "src/PrintVault.Infrastructure/EntityClassificationService.cs").read_text(encoding="utf-8")

checks = [
    ("dimensional model documented", "subject identity" in fusion and "artifact/function" in fusion),
    ("artifact semantics outrank subject entity", "else if (strongSourceDerivedAnalyzer)" in fusion and fusion.index("else if (strongSourceDerivedAnalyzer)") < fusion.index("else if (entity is not null)"),
    ("entity is retained as subject identity", "Subject identity:" in fusion),
    ("entity cannot overwrite established artifact category", "categoryWasUnresolved" in entity),
    ("entity preservation explicitly covers artifact categories", "HueForge, Keychains, Soap Holders" in entity),
    ("entity/category mismatch is not automatically a conflict", "semanticChannelsAgree = strongSourceDerivedAnalyzer" in fusion),
    ("generic lexical evidence remains non-promoting", "Lexical cue retained as non-promoting evidence" in fusion),
    ("production fusion remains active", "ApplyHighConfidenceFusion(m);" in (ROOT / "src/PrintVault.Infrastructure/LibraryEngine.cs").read_text(encoding="utf-8")),
]

failed = [name for name, ok in checks if not ok]
if failed:
    print("SEMANTIC DIMENSIONAL ARBITRATION 9.0.44 FAILED")
    for name in failed:
        print("-", name)
    raise SystemExit(1)

print("SEMANTIC DIMENSIONAL ARBITRATION 9.0.44 PASSED")
