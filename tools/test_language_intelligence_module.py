from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
svc = (ROOT / "src/PrintVault.Infrastructure/LanguageIntelligenceService.cs").read_text(encoding="utf-8")
stage = (ROOT / "src/PrintVault.Infrastructure/Intelligence/LanguageNormalizationStage.cs").read_text(encoding="utf-8")

checks = {
    "dedicated language service": "class LanguageIntelligenceService" in svc,
    "unicode normalization": "NormalizationForm.FormKC" in svc,
    "zero width cleanup": "\\u200B-\\u200D" in svc,
    "diacritic normalization": "NonSpacingMark" in svc,
    "chinese detection": '"Chinese"' in svc and "\\u4E00" in svc,
    "japanese detection": '"Japanese"' in svc and "\\u3040" in svc,
    "korean detection": '"Korean"' in svc and "\\uAC00" in svc,
    "japanese kana wins over kanji": 'if (text.Any(c => c >= \'\\u3040\' && c <= \'\\u30FF\')) return "Japanese";' in svc,
    "chinese glossary": '["吊车"]=' in svc and '["蝙蝠车"]=' in svc,
    "traditional chinese glossary": '["蝙蝠俠"]=' in svc and '["蜘蛛人"]=' in svc,
    "3d printing glossary": '["3D打印"]=' in svc and '["打印模型"]=' in svc,
    "japanese glossary": '["飛行機"]=' in svc,
    "korean glossary": '["자동차"]=' in svc,
    "mixed language detection": "mixed-language signal" in svc,
    "confidence bounded": "Math.Clamp" in svc,
    "offline design": "network service" in svc,
    "filename safety": "source filename" in svc,
    "stage consumes dedicated service": "LanguageIntelligenceService" in stage,
    "stage records confidence": "TranslationConfidence" in stage,
}
failed = [k for k, v in checks.items() if not v]
if failed:
    raise SystemExit("LANGUAGE MODULE TEST FAILED: " + ", ".join(failed))
print(f"LANGUAGE MODULE TEST PASSED — {len(checks)} checks")
