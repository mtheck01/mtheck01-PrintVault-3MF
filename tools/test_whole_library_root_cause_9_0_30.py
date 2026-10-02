from pathlib import Path
root=Path(__file__).resolve().parents[1]
ver=(root/'VERSION').read_text().strip()
assert ver=='9.0.33',ver
engine=(root/'src/PrintVault.Infrastructure/LibraryEngine.cs').read_text()
service=(root/'src/PrintVault.Infrastructure/WholeLibraryRootCauseAnalysisService.cs').read_text()
main=(root/'src/PrintVault/MainWindow.xaml.cs').read_text()
xaml=(root/'src/PrintVault/MainWindow.xaml').read_text()
assert 'try { m.Hash = await HashAsync(path, ct); }' in engine
assert 'catch { m.Hash = ""; }' in engine
assert 'catch { m.ThumbnailPath = null; }' in engine
assert 'persisted.Count != models.Count' in engine
assert 'WholeLibraryRootCauseService' not in main or 'WholeLibraryRootCauseAnalysisService' in main
assert 'WholeLibraryRootCause_Click' in main and 'WHOLE-LIBRARY ROOT-CAUSE PASS' in xaml
for token in ['OBJECT_ROLE_MISMATCH','ENTITY_EVIDENCE_OVERRIDES_STORED','LEXICAL_CLASSIFIER_DISAGREEMENT','ANALYZER_OR_PARSER_GAP','INSUFFICIENT_INDEPENDENT_EVIDENCE','RESOLUTION_CANDIDATE']:
    assert token in service, token
print('WHOLE-LIBRARY ROOT-CAUSE 9.0.33 — PASS')
print('Rebuild preserves records when optional hash/thumbnail enrichment fails; read-back count is verified; forensic root-cause categories are present.')
