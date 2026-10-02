from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
checks = {}
service = (ROOT/'src/PrintVault.Infrastructure/ModelIntelligenceService.cs').read_text()
challenge = (ROOT/'src/PrintVault.Infrastructure/HueForgeContextChallengeService.cs').read_text()
window = (ROOT/'src/PrintVault/ModelIntelligenceHueForgeChallengeWindow.cs').read_text()
xaml = (ROOT/'src/PrintVault/MainWindow.xaml').read_text()
code = (ROOT/'src/PrintVault/MainWindow.xaml.cs').read_text()
checks['version'] = '<Version>9.0.6</Version>' in (ROOT/'src/PrintVault/PrintVault.csproj').read_text()
checks['hueforge context'] = 'IsHueForge' in service and 'ExtractCanvasSize' in service
checks['size not identity'] = 'HueForge size-only' in challenge and 'Independent negative control: different named entities share the same 200x200 HueForge format.' in challenge
checks['printer context only'] = 'Printer-only' in challenge and 'Independent negative control: different named entities share printer/device metadata.' in challenge
checks['same subject different size'] = 'Same subject different size' in challenge
checks['analyze pair'] = 'AnalyzeRelationship' in service
checks['no production writes'] = 'SaveAll' not in service and 'File.Move' not in service and 'File.Delete' not in service
checks['snapshot'] = 'CreateSnapshot' in challenge and 'physical 3MF files unchanged' in challenge
checks['navigation'] = 'HueForge Context Challenge' in xaml and 'ModelIntelligenceHueForgeChallenge_Click' in code
checks['window'] = window.count('{') == window.count('}')
checks['service braces'] = service.count('{') == service.count('}')
checks['challenge braces'] = challenge.count('{') == challenge.count('}')
checks['100 batch UI'] = 'service.Run(100)' in (ROOT/'src/PrintVault/ModelIntelligenceBatchTestWindow.cs').read_text()
failed=[k for k,v in checks.items() if not v]
if failed: raise SystemExit('HUEFORGE CONTEXT TEST STATIC AUDIT FAILED: '+', '.join(failed))
print(f'HUEFORGE CONTEXT STATIC AUDIT PASSED — {len(checks)} checks')
