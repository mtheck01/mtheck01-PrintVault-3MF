from pathlib import Path
import re, sys
root = Path(__file__).resolve().parents[1]
bat = (root / 'build_windows.bat').read_text(encoding='utf-8')
ps = (root / 'tools' / 'release_preflight.ps1').read_text(encoding='utf-8')
version = (root / 'VERSION').read_text(encoding='utf-8').strip()
checks = {
    'version': bool(re.fullmatch(r'\d+\.\d+\.\d+', version)),
    'batch_invokes_root_only': '-File "%ROOT%tools\\release_preflight.ps1" -Root "%ROOT%"' in bat,
    'batch_does_not_require_expected_version_arg': '-ExpectedVersion' not in bat,
    'preflight_reads_version': "ReadText $versionPath" in ps,
    'preflight_expected_version_optional': '[Parameter(Mandatory=$false)][string]$ExpectedVersion' in ps,
    'preflight_returns_result': 'RESULT: PASS' in ps and 'RESULT: FAIL' in ps,
    'batch_echoes_preflight': 'type "%PREFLIGHT_LOG%"' in bat,
    'preflight_checks_challenge_versions': 'Challenge services discovered' in ps and 'No hard-coded BUILD version' in ps and 'No hard-coded report title version' in ps,
}
failed = [k for k,v in checks.items() if not v]
if failed:
    print('RELEASE PREFLIGHT CONTRACT FAILED:', ', '.join(failed))
    raise SystemExit(1)
print(f'RELEASE PREFLIGHT CONTRACT PASSED — {version} — {len(checks)} checks')
