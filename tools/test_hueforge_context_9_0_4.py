from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
service = (ROOT/"src/PrintVault.Infrastructure/ModelIntelligenceService.cs").read_text()
challenge = (ROOT/"src/PrintVault.Infrastructure/HueForgeContextChallengeService.cs").read_text()
window = (ROOT/"src/PrintVault/ModelIntelligenceHueForgeChallengeWindow.cs").read_text()
models = (ROOT/"src/PrintVault.Core/ModelIntelligenceModels.cs").read_text()
proj = (ROOT/"src/PrintVault/PrintVault.csproj").read_text()
checks = {
 "version": "<Version>9.0.13</Version>" in proj,
 "independent engine guard": "IdentityIntersectionCount" in service and "identityIntersection >= 2" in service,
 "same entity expectation": "Independent expectation: both records resolve to the same named entity." in challenge,
 "negative entity control": "Independent negative control: different named entities share the same 200x200 HueForge format." in challenge,
 "printer negative control": "Independent negative control: different named entities share printer/device metadata." in challenge,
 "non-hue negative control": "Independent negative control: different named entities share 200x200 outside HueForge." in challenge,
 "pass fail ui": '"Result", "Result", 70' in window,
 "test basis ui": '"Test Basis", "ExpectedBasis"' in window,
 "result property": 'public string Result => Passed ? "PASS" : "FAIL";' in models,
 "snapshot safety": "CreateSnapshot" in challenge and "physical 3MF files unchanged" in challenge,
 "no production writes": "SaveAll" not in challenge and "File.Move" not in challenge and "File.Delete" not in challenge,
}
failed=[k for k,v in checks.items() if not v]
if failed: raise SystemExit("9.0.13 HUEFORGE ADVERSARIAL STATIC AUDIT FAILED: "+", ".join(failed))
print(f"9.0.13 HUEFORGE ADVERSARIAL STATIC AUDIT PASSED — {len(checks)} checks")
