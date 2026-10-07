from pathlib import Path
root=Path(__file__).parents[1]
svc=(root/'src/PrintVault.Infrastructure/CategoryReconciliationService.cs').read_text()
ui=(root/'src/PrintVault/MainWindow.xaml.cs').read_text()
xaml=(root/'src/PrintVault/MainWindow.xaml').read_text()
checks={
 'legacy-to-canonical mappings': '02_Functional' not in svc and 'functional' in svc and 'multicolor' in svc and 'cosplay' in svc,
 'empty retirement': 'Empty legacy' in svc and 'retireCategory' in svc,
 'protected overrides': 'CategoryOverride' in svc and 'protectedFiles' in svc,
  'catalog update': 'repo.Upsert(model)' in svc and 'model.Category = item.TargetCategory' in svc,
 'per-category retirement accounting': 'changedForItem' in svc and 'models.Count == changed' not in svc,
 'tags updated': 'ReplaceCategoryTag' in svc,
 'persistent undo': 'last_category_reconciliation.json' in svc and 'JsonSerializer' in svc,
 'undo restores category': 'PreviousCategory' in svc and 'PreviousTags' in svc,
 'dedicated UI': 'CategoryReconciliationWindow' in ui and 'ReconcileCategories_Click' in ui,
 'smart cleanup invokes reconciliation': 'ReconcileCategories(showIfClean: true)' in ui,
 'dedicated menu action': 'Reconcile Categories' in xaml and 'Undo Last Category Reconciliation' in xaml,
 'canonical targets': all(x in svc for x in (
        '"multicolor" => "Uncategorized"',
        '"testprint" => "Uncategorized"',
        '"testprints" => "Uncategorized"',
        '"aviation" => "Vehicles"',
        '"aircraft" => "Vehicles"',
        '"cosplay" => "Figures & Characters"',
    )),
}
failed=[k for k,v in checks.items() if not v]
if failed: raise SystemExit('CATEGORY RECONCILIATION TEST FAILED: '+', '.join(failed))
print(f'CATEGORY RECONCILIATION TEST PASSED — {len(checks)} checks')
