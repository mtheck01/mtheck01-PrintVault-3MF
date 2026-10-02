from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
core=(ROOT/"src/PrintVault.Core/LibraryEngine.cs").read_text()
engine=(ROOT/"src/PrintVault.Infrastructure/LibraryEngine.cs").read_text()
xaml=(ROOT/"src/PrintVault/MainWindow.xaml").read_text()
cs=(ROOT/"src/PrintVault/MainWindow.xaml.cs").read_text()
checks={
"enum": "enum ScanMode" in core,
"quick": "ScanMode.Quick" in engine,
"turbo": "ScanMode.Turbo" in engine,
"deep": "ScanMode.Deep" in engine,
"quick_skips_intelligence": "var doIntelligence = mode != ScanMode.Quick" in engine,
"quick_clears_changed_hash": "if (mode == ScanMode.Quick) m.Hash = """ in engine,
"turbo_hashes": "var doHash = mode != ScanMode.Quick" in engine,
"deep_forces_intelligence": "mode == ScanMode.Deep || needsIntelligence" in engine,
"selector": 'x:Name="ScanModeSelector"' in xaml,
"three_modes": all(x in xaml for x in ["Quick Scan","Turbo Scan","Deep Scan"]),
"passes_mode": "ScanAsync(new[] { root }, mode, token, progress)" in cs,
}
failed=[k for k,v in checks.items() if not v]
if failed: raise SystemExit("SCAN MODE TEST FAILED: "+", ".join(failed))
print("SCAN MODE TEST PASSED — "+str(len(checks))+" checks")
