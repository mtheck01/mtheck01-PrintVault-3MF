from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
core=(ROOT/"src/PrintVault.Core/LibraryEngine.cs").read_text(encoding="utf-8")
infra=(ROOT/"src/PrintVault.Infrastructure/LibraryEngine.cs").read_text(encoding="utf-8")
ui=(ROOT/"src/PrintVault/MainWindow.xaml.cs").read_text(encoding="utf-8")
xaml=(ROOT/"src/PrintVault/MainWindow.xaml").read_text(encoding="utf-8")
checks={
"progress model exists":"record ScanProgress" in core,
"discovered field":"int Discovered" in core,
"indexed field":"int Indexed" in core,
"failed field":"int Failed" in core,
"stable discovery denominator":'new ScanProgress("Indexing", files.Count, 0' in infra,
"indexed progress reports":'new ScanProgress("Indexing", files.Count, processed' in infra,
"UI telemetry":'Found {p.Discovered:N0} • Indexed {p.Indexed:N0} • Failed {p.Failed:N0}' in ui,
"progress control":'x:Name="ScanProgressText"' in xaml,
"clear reset":'ScanProgressText.Text = "Ready"' in ui,
}
failed=[k for k,v in checks.items() if not v]
for k,v in checks.items(): print(("PASS" if v else "FAIL")+": "+k)
print("RESULT:","PASS" if not failed else "FAIL")
raise SystemExit(0 if not failed else 1)
