from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
code = (ROOT / "src/PrintVault/MainWindow.xaml.cs").read_text(encoding="utf-8")
checks = {
    "clear is async": "private async Task ClearLibraryAsync()" in code,
    "clear invalidates generation": "Interlocked.Increment(ref scanGeneration)" in code,
    "clear cancels active scan": "scanCts?.Cancel();" in code,
    "clear awaits pending scan": "await pendingScan" in code,
    "clear empties repository after scan shutdown": "engine.Repository.ClearAllModels();" in code,
    "scan captures generation": "var generation = Volatile.Read(ref scanGeneration);" in code,
    "scan guards replacement after cancellation": "if (generation != Volatile.Read(ref scanGeneration)) return;" in code,
    "scan start is tracked": "activeScanTask = ScanAsync();" in code,
    "settings awaits clear": "await ClearLibraryAsync();" in code,
    "settings handler is async": "private async void Settings_Click" in code,
}
failed=[k for k,v in checks.items() if not v]
for k,v in checks.items(): print(("PASS" if v else "FAIL")+": "+k)
print("RESULT:", "PASS" if not failed else "FAIL")
raise SystemExit(0 if not failed else 1)
