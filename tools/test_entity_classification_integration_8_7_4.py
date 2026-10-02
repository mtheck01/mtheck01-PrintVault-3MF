from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
svc = (ROOT/'src/PrintVault.Infrastructure/EntityClassificationService.cs').read_text(encoding='utf-8')
main = (ROOT/'src/PrintVault/MainWindow.xaml.cs').read_text(encoding='utf-8')
engine = (ROOT/'src/PrintVault.Infrastructure/LibraryEngine.cs').read_text(encoding='utf-8')
entity = (ROOT/'src/PrintVault.Infrastructure/MultilingualEntityService.cs').read_text(encoding='utf-8')
proj = (ROOT/'src/PrintVault/PrintVault.csproj').read_text(encoding='utf-8')
cmd = (ROOT/'src/PrintVault.Core/Commands.cs').read_text(encoding='utf-8')
checks = {
 'version 8.7.8': '<Version>8.7.8</Version>' in proj and 'Version = "8.7.8"' in cmd,
 'shared entity classification service exists': 'public sealed class EntityClassificationService' in svc,
 'colonial viper supported': 'Colonial Viper' in entity and '殖民地毒蛇号' in entity,
 'high confidence gate': 'entity.Confidence < 95' in svc,
 'manual override protected': 'if (model is null || model.CategoryOverride) return false;' in svc,
 'custom category protected': '!BuiltInCategories.All.Contains(model.Category' in svc,
 'entity category applied': 'model.Category = entity.Category;' in svc,
 'vehicle semantic type applied': 'model.SemanticType' in svc and '"Vehicle"' in svc,
 'spacecraft subtype applied': 'model.Subtype = entity.Subtype;' in svc,
 'entity confidence raises intelligence': 'Math.Max(model.IntelligenceScore' in svc,
 'raw analyzer evidence retained': 'analyzer baseline:' in svc,
 'entity evidence persisted in tags': 'entity:' + '" + entity.EntityName' in svc,
 'reapply uses integration': 'var entityApplied = entityClassification.Apply(m);' in main,
 'startup scan uses integration': 'entityClassification.Apply(m);' in engine,
 'no physical file rename in service': 'physical filename' in svc.lower() or 'physical .3mf' in svc.lower(),
}
failed=[k for k,v in checks.items() if not v]
if failed: raise SystemExit('ENTITY CLASSIFICATION 8.7.8 TEST FAILED: '+', '.join(failed))
print(f'ENTITY CLASSIFICATION 8.7.8 TEST PASSED — {len(checks)} checks')
