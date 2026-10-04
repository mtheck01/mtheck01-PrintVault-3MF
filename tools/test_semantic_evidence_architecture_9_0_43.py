from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
fusion = (ROOT / "src/PrintVault.Infrastructure/SemanticEvidenceFusionService.cs").read_text(encoding="utf-8")
engine = (ROOT / "src/PrintVault.Infrastructure/LibraryEngine.cs").read_text(encoding="utf-8")
pipeline = (ROOT / "src/PrintVault.Infrastructure/ModelIntelligencePipeline.cs").read_text(encoding="utf-8")

assert "Separate actionable contradiction from absence of evidence." in fusion
assert 'Unresolved: no sufficiently specific semantic classification was established' in fusion
assert 'Classification agrees with stored category; no actionable conflict' in fusion
assert 'var actionableConflict = categoriesDiffer' in fusion
assert 'Subject identity:' in fusion
assert 'strongSourceDerivedAnalyzer' in fusion
assert 'strongSourceDerivedAnalyzer' in fusion

# The semantic fusion engine must be part of the actual production rebuild path,
# not merely the forensic report. This was the key architectural gap exposed by 9.0.42.
assert 'private readonly ModelIntelligencePipeline intelligencePipeline = new();' in engine
assert 'intelligencePipeline.Apply(m);' in engine
assert 'entityClassification.Apply(m);' in pipeline
assert 'fusion.Fuse(model, entity);' in pipeline
assert 'var lexicalActionable = lexicalBasis' in engine
assert 'result.ClassificationConfidence >= 70' in engine
assert 'result.EvidenceQuality >= 40' in engine
assert 'var structuralActionable = structuralBasis' in engine
assert 'result.ClassificationConfidence >= 85' in engine
assert 'result.EvidenceQuality >= 75' in engine

print("PASS: 9.0.43 deep semantic architecture gate")
