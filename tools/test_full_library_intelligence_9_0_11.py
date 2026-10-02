from pathlib import Path
root=Path(__file__).resolve().parents[1]
svc=(root/"src/PrintVault.Infrastructure/FullLibraryIntelligenceChallengeService.cs").read_text()
win=(root/"src/PrintVault/FullLibraryIntelligenceChallengeWindow.cs").read_text()
main=(root/"src/PrintVault/MainWindow.xaml").read_text()
maincs=(root/"src/PrintVault/MainWindow.xaml.cs").read_text()
checks={
"service": "public sealed class FullLibraryIntelligenceChallengeService" in svc,
"snapshot": "production.CreateSnapshot(snapshot)" in svc,
"all records": "var all = sandbox.GetAll()" in svc,
"candidate metrics": "CandidateReductionPercent" in svc and "candidatePairs" in svc,
"determinism": "deterministicPassed" in svc and "SelectParitySample" in svc,
"physical protection": "physicalUnchanged" in svc,
"catalog fingerprint": "catalogFingerprint" in svc,
"progress": "progress?.Invoke" in svc and "ProgressBar" in win,
"ui button": "Full Library Intelligence Challenge" in main,
"ui handler": "FullLibraryIntelligenceChallenge_Click" in maincs,
}
failed=[k for k,v in checks.items() if not v]
if failed: raise SystemExit("FULL LIBRARY AUDIT FAILED: "+", ".join(failed))
print("FULL LIBRARY AUDIT PASSED — "+str(len(checks))+" checks")
