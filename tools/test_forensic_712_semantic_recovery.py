from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
fusion = (ROOT / "src/PrintVault.Infrastructure/SemanticEvidenceFusionService.cs").read_text(encoding="utf-8")
rootcause = (ROOT / "src/PrintVault.Infrastructure/WholeLibraryRootCauseAnalysisService.cs").read_text(encoding="utf-8")

checks = [
    ("compact creature aliases from unresolved corpus", all(x in fusion for x in ['"articulatedotter"', '"flexifrog"', '"flexipangolin"', '"capibara"', '"giraffe"', '"hippo"'])),
    ("common unresolved prop aliases", all(x in fusion for x in ['"crosbow"', '"greatsaber"', '"darksaber"', '"fireaxe"'])),
    ("specific vehicle aliases from unresolved corpus", all(x in fusion for x in ['"m18 hellcat"', '"sr-71"', '"dragonlady"', '"f-100"', '"golf cart"'])),
    ("Chinese vehicle terms are not bundled with generic print/animal terms", 'new[]{"打印","航空母舰","航空","战机","飞机","轰炸机","舰","军舰","医疗舰","坦克","龙","蛇","熊","狗","猫","鸟","蜥蜴","剑","雕塑","挂钩","收纳","支架","举升机","龙门"}, "Vehicles"' not in fusion and '"打印"' not in fusion),
    ("Chinese cue matching supports unspaced CJK text", "CJK text commonly has no whitespace" in fusion and "text.Contains(phrase, StringComparison.OrdinalIgnoreCase)" in fusion),
    ("root-cause attribution receives source-enriched record", "DetermineCause(m, sourceModel, entity, f, conflict)" in rootcause),
    ("root-cause attribution uses enriched analyzer reason", "analyzed.IntelligenceReason.Contains(\"No strong semantic signal\"" in rootcause),
    ("original override remains protected in diagnostic", "if (original.CategoryOverride) return \"MANUAL_CATEGORY_OVERRIDE_CONFLICT\";" in rootcause),
]
failed = [name for name, ok in checks if not ok]
for name, ok in checks:
    print(("PASS: " if ok else "FAIL: ") + name)
if failed:
    raise SystemExit("FORENSIC 712 SEMANTIC RECOVERY FAILED: " + ", ".join(failed))
print(f"FORENSIC 712 SEMANTIC RECOVERY PASSED — {len(checks)} checks")
