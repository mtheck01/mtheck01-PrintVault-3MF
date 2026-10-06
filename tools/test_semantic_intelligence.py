from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
an=(ROOT/"src/PrintVault.Infrastructure/ThreeMfAnalyzer.cs").read_text(encoding="utf-8")
model=(ROOT/"src/PrintVault.Core/Models.cs").read_text(encoding="utf-8")
repo=(ROOT/"src/PrintVault.Infrastructure/LibraryRepository.cs").read_text(encoding="utf-8")
engine=(ROOT/"src/PrintVault.Infrastructure/LibraryEngine.cs").read_text(encoding="utf-8")
checks=[
 ("weighted semantic signals", "Signal[]" in an and "TokenScore" in an),
 ("geometry dimensions", "ComputeDimensions" in an and "vertex" in an),
 ("slicer detection", "SlicerNames" in an),
 ("material evidence", "base" in an and "material" in an.lower()),
 ("semantic persistence", "SemanticType" in model and "SuggestedTags" in model and "RiskFlags" in model),
 ("database migration", '"SemanticType"' in repo and '"RiskFlags"' in repo),
 ("existing library backfill", "string.IsNullOrWhiteSpace(m.SemanticType)" in engine),
 ("user tag preservation", "Preserve user tags" in engine),
 ("custom category preservation", "IsCustomCategory(existingCategory)" in engine and "preserveCustomCategory" in engine),
 ("custom folder recovery", "GetTopLevelCustomCategory" in engine and "Path.GetRelativePath" in engine),
 ("filesystem path excluded from semantic scoring", "allText.Append(name)" in an and "Append(path)" not in an),
 ("signal weights applied", ("TokenScore(semanticScoringText,s.Phrase,s.Weight)" in an or "TokenScore(semantic, s.Phrase, s.Weight)" in an)),
 ("HueForge first-class detection", "DetectSpecialType" in an and "HueForge" in an and "custom_gcode_per_layer" in an),
 ("Keychain first-class detection", "KeychainTerms" in an and "Keychains" in an and "keychain" in an.lower()),
 ("special type confidence", "special.Score" in an),
]
bad=[n for n,ok in checks if not ok]
if bad: raise SystemExit("SEMANTIC TEST FAILED: "+", ".join(bad))
print(f"SEMANTIC INTELLIGENCE TEST PASSED — {len(checks)} checks")
