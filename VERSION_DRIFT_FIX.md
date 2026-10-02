# 9.0.20 — Challenge Version Drift Fix

## Root cause
The application version was already authoritative through `VERSION` → `Directory.Build.props` → assembly metadata → `AppVersion.Version`, but the Engineering/Test Lab challenge services independently hard-coded historical report versions.

That allowed a 9.0.20 executable to display `9.0.20` while Full Library Intelligence reported `BUILD: 9.0.13`. The mismatch was a reporting/version-provenance defect, not evidence that the 9.0.13 intelligence engine was being executed.

## Fixed
All seven challenge services now derive report `BUILD:` lines and report titles from `AppVersion.Version`:

- Entity Semantic Regression
- HueForge Adversarial Regression
- Relationship Identity Gate
- Semantic + Relationship Integration
- Full Library Intelligence
- Semantic Coverage & Confidence
- Semantic Evidence Convergence

The Entity Semantic Regression window summary and the Semantic Evidence Convergence tooltip no longer contain stale historical versions.

## Regression prevention
Release preflight now checks every `*ChallengeService.cs` for:

1. use of `AppVersion.Version`;
2. absence of hard-coded `BUILD: x.y.z` strings;
3. absence of hard-coded `PrintVault 3MF x.y.z` report titles;
4. absence of stale version strings in user-facing PrintVault XAML/C# files.

`tools/test_challenge_version_consistency.py` provides the same source-level gate and is intended to catch version drift before packaging.

## Verification
- Challenge version consistency: PASS — 7 services
- Release preflight contract: PASS — 8 checks
- Static audit: PASS — PrintVault 9.0.20
- UI feature audit: PASS — 8 checks
- No hard-coded challenge report versions remain in `src/PrintVault.Infrastructure`.
- No hard-coded historical challenge versions remain in current PrintVault UI code.

A Windows build/run is still required to validate the final executable and installer because this environment does not contain the Windows .NET SDK/toolchain.
