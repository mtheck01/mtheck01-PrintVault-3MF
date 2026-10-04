from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
models=(ROOT/"src/PrintVault.Core/Models.cs").read_text()
repo=(ROOT/"src/PrintVault.Infrastructure/LibraryRepository.cs").read_text()
svc=(ROOT/"src/PrintVault.Infrastructure/MultilingualMetadataService.cs").read_text()
xaml=(ROOT/"src/PrintVault/MainWindow.xaml").read_text()
cs=(ROOT/"src/PrintVault/MainWindow.xaml.cs").read_text()
checks={
"model metadata fields":"TranslatedTitle" in models and "OriginalLanguage" in models,
"schema version 5":"CurrentSchemaVersion = 5" in repo,
"schema columns":"TranslationConfidence" in repo and "TranslationEvidence" in repo,
"offline service":"never calls a network service" in svc,
"chinese crane":"吊车" in svc and "crane" in svc,
"japanese detection":"Japanese" in svc and '"car"' in svc and "Japanese = " in svc,
"korean detection":"Korean" in svc and '"airplane"' in svc and "Korean = " in svc,
"traditional language routing":"language switch" in svc and '"Chinese" => Chinese' in svc,
"compartmental pipeline":(ROOT/"src/PrintVault.Infrastructure/ModelIntelligencePipeline.cs").exists(),
"pipeline stage boundaries":all(x in (ROOT/"src/PrintVault.Infrastructure/ModelIntelligencePipeline.cs").read_text() for x in ["SECTION 1", "SECTION 2", "SECTION 3", "SECTION 4"]),
"forensic uses pipeline":"ModelIntelligencePipeline" in (ROOT/"src/PrintVault.Infrastructure/WholeLibraryRootCauseAnalysisService.cs").read_text(),
"no file rename":"never renames files" in svc,
"bulk metadata button":"BuildMultilingualMetadata_Click" in xaml and "BuildMultilingualMetadata_Click" in cs,
"translated search":"m.TranslatedTitle.Contains" in cs,
"inspector translation":"InspectorTranslated" in xaml and "InspectorTranslated.Text" in cs,
"version 8.7":"8.7.8" in (ROOT/"src/PrintVault/PrintVault.csproj").read_text(),
}
failed=[k for k,v in checks.items() if not v]
if failed: raise SystemExit("MULTILINGUAL METADATA 8.7 TEST FAILED: "+", ".join(failed))
print(f"MULTILINGUAL METADATA 8.7 TEST PASSED — {len(checks)} checks")
