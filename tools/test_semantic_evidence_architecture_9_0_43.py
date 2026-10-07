from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
fusion = (ROOT / "src/PrintVault.Infrastructure/SemanticEvidenceFusionService.cs").read_text(encoding="utf-8")
engine = (ROOT / "src/PrintVault.Infrastructure/LibraryEngine.cs").read_text(encoding="utf-8")
pipeline = (ROOT / "src/PrintVault.Infrastructure/ModelIntelligencePipeline.cs").read_text(encoding="utf-8")
stage_root = ROOT / "src/PrintVault.Infrastructure/Intelligence"

language = (stage_root / "LanguageNormalizationStage.cs").read_text(encoding="utf-8")
entity_recognition = (stage_root / "EntityRecognitionStage.cs").read_text(encoding="utf-8")
semantic_fusion = (stage_root / "SemanticFusionStage.cs").read_text(encoding="utf-8")
arbitration = (stage_root / "EvidenceArbitrationStage.cs").read_text(encoding="utf-8")

assert "Separate actionable contradiction from absence of evidence." in fusion
assert 'Unresolved: no sufficiently specific semantic classification was established' in fusion
assert 'Classification agrees with stored category; no actionable conflict' in fusion
assert 'var actionableConflict = categoriesDiffer' in fusion
assert 'Subject identity:' in fusion
assert 'strongSourceDerivedAnalyzer' in fusion

# The production engine must invoke the modular pipeline.
assert 'private readonly ModelIntelligencePipeline intelligencePipeline = new();' in engine
assert 'intelligencePipeline.Apply(m);' in engine

# Each intelligence responsibility has an explicit stage boundary.
assert 'new LanguageNormalizationStage()' in pipeline
assert 'new EntityClassificationStage()' not in pipeline
assert 'EntityClassificationService' not in pipeline
assert 'new EntityRecognitionStage()' in pipeline
assert 'new SemanticFusionStage()' in pipeline
assert 'new EvidenceArbitrationStage()' in pipeline
assert 'foreach (var stage in stages)' in pipeline
assert 'if (stage is EntityClassificationStage)' not in pipeline

# Stage contracts prove that responsibilities are no longer embedded in the orchestrator.
assert 'service.Apply(context.Model);' in language
assert 'AddTag(context.Model.SuggestedTags' in entity_recognition
assert 'AddTag(context.Model.Tags' in entity_recognition
assert 'service.Recognize(context.Model);' in entity_recognition
assert 'service.Fuse(context.Model, context.Entity);' in semantic_fusion
assert 'var actionable =' in arbitration
assert 'model.Category = result.Category;' in arbitration

# The arbitration thresholds remain unchanged, but now live exclusively in the
# evidence-arbitration section rather than the pipeline orchestrator.
assert 'result.ClassificationConfidence >= 85' in arbitration
assert 'result.EvidenceQuality >= 75' in arbitration
assert 'result.ClassificationConfidence >= 70' in arbitration
assert 'result.EvidenceQuality >= 40' in arbitration
assert 'result.ClassificationConfidence >= 60' in arbitration
assert 'result.EvidenceQuality >= 25' in arbitration

print("PASS: 9.0.43 modular semantic architecture gate")
