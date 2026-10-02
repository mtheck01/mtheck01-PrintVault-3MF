from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
main=(ROOT/"src/PrintVault/MainWindow.xaml.cs").read_text(encoding="utf-8")
ver=(ROOT/"src/PrintVault.Core/Commands.cs").read_text(encoding="utf-8")
proj=(ROOT/"src/PrintVault/PrintVault.csproj").read_text(encoding="utf-8")
win=(ROOT/"src/PrintVault/LibrarySettingsWindow.cs").read_text(encoding="utf-8")
checks={
 "version":"8.7.8" in ver and "<Version>8.7.8</Version>" in proj,
 "no production fallback":"return Directory.Exists(@\"D:\\3d print files\")" not in main,
 "production guard":"IsForbiddenProductionLibrary" in main and "Production Library Protected" in main,
 "test default":"D:\\3d print files test" in main and "D:\\3d print files test" in win,
 "clear library":"ClearLibrary" in main and "ClearRequested" in win,
 "settings window":"LibrarySettingsWindow" in main,
 "custom category preserved":"CategoryOverride" in main or "CategoryOverride" in (ROOT/"src/PrintVault.Infrastructure/LibraryEngine.cs").read_text(encoding="utf-8"),
}
for k,v in checks.items(): print(("PASS" if v else "FAIL")+": "+k)
print("RESULT:","PASS" if all(checks.values()) else "FAIL")
raise SystemExit(0 if all(checks.values()) else 1)
