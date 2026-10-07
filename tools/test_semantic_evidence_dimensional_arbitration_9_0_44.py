from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
fusion = (ROOT / "src/PrintVault.Infrastructure/SemanticEvidenceFusionService.cs").read_text(encoding="utf-8")
engine = (ROOT / "src/PrintVault.Infrastructure/LibraryEngine.cs").read_text(encoding="utf-8")
root_cause = (ROOT / "src/PrintVault.Infrastructure/WholeLibraryRootCauseAnalysisService.cs").read_text(encoding="utf-8")

# 9.0.44 semantic architecture contract:
# - subject identity describes what the model depicts;
# - artifact/function describes what printable object it is;
# - catalog category is selected from artifact evidence;
# - entity recognition must not overwrite an already-established artifact category.
checks = [
    ("dimensional model documented", "subject identity" in fusion and "artifact/function" in fusion),
    ("strong source-derived artifact arbitration exists", "else if (strongSourceDerivedAnalyzer)" in fusion),
    ("entity is retained as subject identity", "Subject identity:" in fusion),
    ("unresolved stored categories are explicitly recognized", "storedCategoryIsUnresolved" in fusion),
    ("actionable category conflicts exclude unresolved stored categories", "!storedCategoryIsUnresolved" in fusion),
    ("entity mismatch is not automatically an artifact conflict", "semanticChannelsAgree = strongSourceDerivedAnalyzer" in fusion),
    ("semantic-channel agreement is explicit", "semanticChannelsAgree" in fusion),
    ("strong alternatives require actionable disagreement", "strongAlternative && categoriesDiffer && !storedCategoryIsUnresolved" in fusion),
    ("unresolved categories are excluded from forensic conflicts", "!IsUnresolvedCategory(stored)" in root_cause and "!IsUnresolvedCategory(inferred)" in root_cause),
    ("production rebuild uses modular intelligence pipeline", "intelligencePipeline.Apply(m);" in engine),
    ("production semantic fusion stage exists and invokes fusion service", (ROOT / "src/PrintVault.Infrastructure/Intelligence/SemanticFusionStage.cs").exists() and "service.Fuse(" in (ROOT / "src/PrintVault.Infrastructure/Intelligence/SemanticFusionStage.cs").read_text(encoding="utf-8")),
    ("production arbitration stage writes fused artifact classification", "model.Category = result.Category;" in (ROOT / "src/PrintVault.Infrastructure/Intelligence/EvidenceArbitrationStage.cs").read_text(encoding="utf-8")),
]

failed = [name for name, ok in checks if not ok]
for name, ok in checks:
    print(("PASS: " if ok else "FAIL: ") + name)

if failed:
    print("SEMANTIC DIMENSIONAL ARBITRATION 9.0.44 FAILED")
    for name in failed:
        print("-", name)
    raise SystemExit(1)

print("SEMANTIC DIMENSIONAL ARBITRATION 9.0.44 PASSED")
