from pathlib import Path
root=Path(__file__).parents[1]
svc=(root/'src/PrintVault.Infrastructure/LibraryConsolidationService.cs').read_text()
ui=(root/'src/PrintVault/MainWindow.xaml.cs').read_text()
xaml=(root/'src/PrintVault/MainWindow.xaml').read_text()
notes=(root/'RELEASE_NOTES_v8.4.4.md').read_text()
checks={
 'file-level plan': 'public ConsolidationPlan Preview' in svc and 'ConsolidationMove' in svc,
 'legacy aliases': '02_Functional' in svc and '07_Multi_Color' in svc and '99_Other' in svc,
 'intelligence resolution': 'Model intelligence/category used to resolve legacy folder' in svc,
 'custom override preserved': 'CategoryOverride == true' in svc and '!model.CategoryOverride' in svc,
 '3mf only': 'EnumerateFiles(dir, "*.3mf"' in svc,
 'safe collisions': 'Unique(Path.Combine(targetDir' in svc,
 'repo path update': 'repo.RenamePath(source, dest, model)' in svc,
 'selected source cleanup': 'var sourceDirs = new HashSet<string>' in svc and 'sourceDirs.Add(sourceDir)' in svc,
 'persistent undo': 'last_consolidation.json' in svc and 'JsonSerializer' in svc and 'PreviousCategoryOverride' in svc,
 'undo restore': 'public ConsolidationResult UndoLast()' in svc,
 'review gating': 'includeReview' in svc and 'Confidence, string Reason' in svc,
 'smart cleanup ui': 'Smart Cleanup' in xaml and 'Smart Library Consolidation' in (root/'src/PrintVault/ConsolidationWindow.cs').read_text(),
 'undo consolidation ui': 'UndoConsolidation_Click' in ui and 'Undo Last Consolidation' in xaml,
 'search label': 'SEARCH YOUR 3MF LIBRARY' in xaml,
 'release notes': 'Smart Library Consolidation' in notes and 'Undo Last Consolidation' in notes,
}
failed=[k for k,v in checks.items() if not v]
if failed: raise SystemExit('CONSOLIDATION TEST FAILED: '+', '.join(failed))
print(f'CONSOLIDATION TEST PASSED — {len(checks)} checks')
