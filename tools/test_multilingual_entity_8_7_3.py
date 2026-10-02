from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
svc = (ROOT / 'src/PrintVault.Infrastructure/MultilingualEntityService.cs').read_text(encoding='utf-8')
smart = (ROOT / 'src/PrintVault.Infrastructure/SmartCategoryReconciliationService.cs').read_text(encoding='utf-8')
meta = (ROOT / 'src/PrintVault.Infrastructure/MultilingualMetadataService.cs').read_text(encoding='utf-8')
main = (ROOT / 'src/PrintVault/MainWindow.xaml.cs').read_text(encoding='utf-8')
proj = (ROOT / 'src/PrintVault/PrintVault.csproj').read_text(encoding='utf-8')
cmd = (ROOT / 'src/PrintVault.Core/Commands.cs').read_text(encoding='utf-8')
checks = {
    'version 8.7.8': '<Version>8.7.8</Version>' in proj and 'Version = "8.7.8"' in cmd,
    'exact Chinese Colonial Viper phrase': '殖民地毒蛇号' in svc and '殖民地毒蛇' in svc,
    'canonical entity': 'Colonial Viper' in svc,
    'franchise domain': 'Battlestar Galactica' in svc,
    'vehicle category': '"Vehicles"' in svc and 'Spacecraft / Fighter' in svc,
    'entity confidence separate from translation': 'entity confidence' in svc and 'TranslationConfidence' not in svc,
    'offline recognition': 'never renames or modifies' in svc and 'network' not in svc.lower(),
    'entity metadata appended without replacing translation confidence': 'model.TranslationEvidence += entity.Evidence' in meta and 'model.TranslationConfidence = result.Confidence' in meta,
    'smart classifier uses entity': 'new MultilingualEntityService().Recognize(model)' in smart and 'entity.Evidence' in smart,
    'translated title included in classifier': 'translatedTitle' in smart and 'model.TranslatedTitle' in smart,
    'inspector shows entity separately': 'Entity: {entity.EntityName}' in main and 'entity confidence' in main,
    'manual override still protected': 'if (model.CategoryOverride) return null;' in smart,
    'original filename remains untouched': 'never renames' in svc,
}
failed=[k for k,v in checks.items() if not v]
if failed:
    raise SystemExit('MULTILINGUAL ENTITY 8.7.8 TEST FAILED: ' + ', '.join(failed))
print(f'MULTILINGUAL ENTITY 8.7.8 TEST PASSED — {len(checks)} checks')
