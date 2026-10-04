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
assert 'entityClassification.Apply(model);' in pipeline
assert 'fusion.Fuse(model, entity);' in pipeline
# Arbitration thresholds belong to the modular intelligence pipeline now.
# LibraryEngine is responsible for invoking the pipeline; the pipeline owns
# lexical/structural evidence arbitration and therefore is the authoritative
# production path this gate must validate.
assert 'var structuralBasis = result.Basis.Contains' in pipeline
assert 'var lexicalBasis = result.Basis.Contains' in pipeline
assert 'var lexicalHits = result.Evidence.Count' in pipeline
assert 'result.ClassificationConfidence >= 85' in pipeline
assert 'result.EvidenceQuality >= 75' in pipeline
assert 'result.ClassificationConfidence >= 70' in pipeline
assert 'result.EvidenceQuality >= 40' in pipeline
assert 'var actionable = (structuralBasis' in pipeline
assert 'if (!actionable) return entity is not null;' in pipeline
assert 'model.Category = result.Category;' in pipeline

print("PASS: 9.0.43 deep semantic architecture gate")
