from pathlib import Path
root=Path(__file__).parents[1]
svc=(root/'src/PrintVault.Infrastructure/SmartCategoryReconciliationService.cs').read_text(encoding='utf-8')
models=(root/'src/PrintVault.Core/Models.cs').read_text(encoding='utf-8')
ui=(root/'src/PrintVault/MainWindow.xaml.cs').read_text(encoding='utf-8')
xaml=(root/'src/PrintVault/MainWindow.xaml').read_text(encoding='utf-8')
checks={
 'atomic metadata-only service': 'never moves files' in svc and 'repo.SaveAll(candidates.Values' in svc and 'File.Move' not in svc,
 'protected overrides': 'CategoryOverride' in svc and 'Protected' in svc,
 'confidence': 'ConfidencePercent' in svc and 'confidence < 70' in svc and 'Math.Min(confidence, 82)' in svc,
 'terrain categories': 'Buildings' in svc and 'Nature & Scenery' in svc and 'Tabletop Terrain' in svc and 'Props & Accessories' in svc,
 'semantic evidence': 'SemanticType' in svc and 'Subtype' in svc and 'SuggestedTags' in svc,
 'persistent undo': 'last_smart_category_review.json' in svc and 'SmartCategoryUndoManifest' in svc,
 'learned corrections': 'learn?.Invoke' in svc,
 'built-in categories': '"Buildings"' in models and '"Tabletop Terrain"' in models,
 'review UI': 'SmartCategoryReviewWindow' in ui and 'SmartCategoryReview_Click' in ui,
 'undo UI': 'UndoSmartCategoryReview_Click' in ui,
 'menu': 'Smart Category Review' in xaml,
 'duplicate manager grid': 'MinWidth = 900' in (root/'src/PrintVault/DuplicateManagerWindow.cs').read_text(encoding='utf-8') and 'HorizontalContentAlignment = HorizontalAlignment.Stretch' in (root/'src/PrintVault/DuplicateManagerWindow.cs').read_text(),
}
failed=[k for k,v in checks.items() if not v]
if failed: raise SystemExit('SMART CATEGORY REVIEW TEST FAILED: '+', '.join(failed))
print(f'SMART CATEGORY REVIEW TEST PASSED — {len(checks)} checks')
