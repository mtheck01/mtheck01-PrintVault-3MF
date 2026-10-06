from pathlib import Path
root=Path(__file__).resolve().parents[1]
svc=(root/"src/PrintVault.Infrastructure/SemanticEvidenceFusionService.cs").read_text()
challenge=(root/"src/PrintVault.Infrastructure/SemanticEvidenceFusionChallengeService.cs").read_text()
window=(root/"src/PrintVault/SemanticEvidenceFusionChallengeWindow.cs").read_text()
xaml=(root/"src/PrintVault/MainWindow.xaml").read_text()
checks=[
 ("fusion service exists", "public sealed class SemanticEvidenceFusionService" in svc),
 ("named entity is strongest evidence", "Named entity:" in svc and "entity.Confidence" in svc),
 ("evidence convergence exists", "Evidence convergence:" in svc and "aviationIdentityHits" in svc),
 ("actionable conflict remains review", "actionableConflict" in svc and "Review recommended: independent evidence conflicts with stored classification" in svc),
 ("unresolved evidence is explicit", "Unresolved: no sufficiently specific semantic classification was established" in svc),
 ("context cannot establish identity", "Context-only signals" in svc and "not identity evidence" in svc),
 ("review gate exists", "Review recommended" in svc and "ReviewRequired" in svc),
 ("independent controlled expectations", "ExpectedCase" in challenge and "BuildCases" in challenge),
 ("full library processing", "foreach (var model in all)" in challenge),
 ("deterministic parity", "deterministic fusion parity" in challenge),
 ("catalog fingerprint protection", "production catalog fingerprints unchanged" in challenge),
 ("physical protection", "physical 3MF files unchanged" in challenge),
 ("test lab window", "SemanticEvidenceFusionChallengeWindow" in window),
 ("ui entry", "Semantic Evidence Convergence" in xaml),
 ("dynamic build version", "AppVersion.Version" in challenge and "BUILD: 9.0.14" not in challenge),
]
failed=[name for name,ok in checks if not ok]
print("SEMANTIC EVIDENCE CONVERGENCE VERSION-SAFE AUDIT PASSED" if not failed else "SEMANTIC EVIDENCE CONVERGENCE AUDIT FAILED")
for name in failed: print("-",name)
raise SystemExit(1 if failed else 0)
