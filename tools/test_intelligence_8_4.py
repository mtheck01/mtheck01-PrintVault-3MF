from pathlib import Path
root=Path(__file__).parents[1]
models=(root/'src/PrintVault.Core/Models.cs').read_text()
an=(root/'src/PrintVault.Infrastructure/ThreeMfAnalyzer.cs').read_text()
repo=(root/'src/PrintVault.Infrastructure/LibraryRepository.cs').read_text()
eng=(root/'src/PrintVault.Infrastructure/LibraryEngine.cs').read_text()
ui=(root/'src/PrintVault/MainWindow.xaml.cs').read_text()
xaml=(root/'src/PrintVault/MainWindow.xaml').read_text()
checks={'print_method_model':'PrintMethod' in models and 'PrintMethodConfidence' in models,'print_method_detection':'DetectPrintMethod' in an and 'FDM' in an and 'Resin' in an,'unknown_method':'"Unknown"' in an,'both_method':'"Both"' in an,'evidence':'PrintMethodEvidence' in models and 'PrintMethodEvidence' in repo,'special_type':'SpecialType' in models and 'SpecialType' in repo,'category_override':'CategoryOverride' in models and 'CategoryOverride' in eng,'print_override':'PrintMethodOverride' in models and 'PrintMethodOverride' in eng,'filter_method':'PrintMethodFilter' in xaml and 'PrintMethodFilter' in ui,'filter_special':'SpecialFilter' in xaml and 'SpecialFilter' in ui,'search_intelligence':'m.IntelligenceReason.Contains' in ui and 'm.SemanticType.Contains' in ui,'migration_schema_current':'CurrentSchemaVersion = 5' in repo}
failed=[k for k,v in checks.items() if not v]
if failed: raise SystemExit('8.4 INTELLIGENCE TEST FAILED: '+', '.join(failed))
print(f'8.4 INTELLIGENCE TEST PASSED — {len(checks)} checks')
