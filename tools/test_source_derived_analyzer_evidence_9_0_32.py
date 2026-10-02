from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
assert (ROOT / "VERSION").read_text().strip() == "9.0.33"
f = (ROOT / "src/PrintVault.Infrastructure/SemanticEvidenceFusionService.cs").read_text()

assert "TryGetSourceDerivedAnalyzerEvidence" in f
assert "model.IntelligenceScore < 0.35" in f
assert "AnalyzerCategories.Contains(candidate)" in f
assert "Source-derived analyzer semantics" in f
assert "model.Tags" not in f.split("var text =", 1)[1].split(";", 1)[0]
assert "model.SuggestedTags" not in f.split("var text =", 1)[1].split(";", 1)[0]
assert 'convergenceLabels.Add("stored classification")' not in f
print("SOURCE-DERIVED ANALYZER EVIDENCE 9.0.33 — PASS")
print("Clean analyzer semantics may corroborate classification without classifier-output lexical feedback.")
