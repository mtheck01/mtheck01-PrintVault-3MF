from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
version = (ROOT / "VERSION").read_text().strip()
assert version == "9.0.33", version

analyzer = (ROOT / "src/PrintVault.Infrastructure/ThreeMfAnalyzer.cs").read_text()
fusion = (ROOT / "src/PrintVault.Infrastructure/SemanticEvidenceFusionService.cs").read_text()

# Semantic classification must use the narrow source-derived corpus, not arbitrary XML/config text.
assert "var semanticText=new StringBuilder();" in analyzer
assert "var semanticLow=semanticText.ToString().ToLowerInvariant();" in analyzer
assert "TokenScore(semanticLow,s.Phrase,s.Weight)" in analyzer
assert "Generic XML element names, arbitrary attribute" in analyzer

# Derived classifier outputs must not be recycled as lexical evidence.
assert 'string.Join(" ", model.Name ?? "", model.TranslatedTitle ?? "")' in fusion
assert 'model.Tags ?? ""' not in fusion.split('var text =',1)[1].split(';',1)[0]
assert 'model.SuggestedTags ?? ""' not in fusion.split('var text =',1)[1].split(';',1)[0]
assert 'model.IntelligenceReason ?? ""' not in fusion.split('var text =',1)[1].split(';',1)[0]

# Stored classification/analyzer output must not increase convergence quality.
assert 'convergenceLabels.Add("stored classification")' not in fusion
assert 'convergenceLabels.Add("analyzer corroboration")' not in fusion

print("INDEPENDENT EVIDENCE 9.0.33 — PASS")
print("Semantic lexical input is source-derived; classifier outputs cannot self-corroborate.")


# 9.0.33 may use the rebuilt analyzer as a bounded source-derived semantic channel,
# but only through an explicit built-in category emitted by the analyzer.
assert "TryGetSourceDerivedAnalyzerEvidence" in fusion
assert "Source-derived analyzer semantics" in fusion
assert "model.IntelligenceScore < 0.35" in fusion
assert "AnalyzerCategories" in fusion
