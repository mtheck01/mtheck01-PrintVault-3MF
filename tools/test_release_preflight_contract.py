from pathlib import Path
import re, sys
root = Path(__file__).resolve().parents[1]
bat = (root / 'build_windows.bat').read_text(encoding='utf-8')
ps = (root / 'tools' / 'release_preflight.ps1').read_text(encoding='utf-8')
workflow = (root / '.github' / 'workflows' / 'windows-build.yml').read_text(encoding='utf-8')
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
    'installer_smoke_runs_on_windows_hosted_runner': 'runs-on: windows-latest' in workflow and 'Install and launch on isolated admin runner' in workflow,
    'installer_smoke_requires_admin_token': '[Security.Principal.WindowsBuiltInRole]::Administrator' in workflow,
    'installer_smoke_installs_built_artifact': 'dist\\installer\\PrintVault_3MF_v$($version)_Setup.exe' in workflow and 'PrivilegesRequired=admin' in (root / 'installer' / 'PrintVault_3MF.iss').read_text(encoding='utf-8'),
    'installer_smoke_uses_isolated_library_root': 'PrintVault-Isolated-Test-Library' in workflow and '$env:RUNNER_TEMP' in workflow,
    'installer_smoke_requires_responsive_window': '$app.MainWindowHandle' in workflow and '$app.Responding' in workflow and 'INSTALL_SMOKE_WINDOW=PASS' in workflow,
}
failed = [k for k,v in checks.items() if not v]
if failed:
    print('RELEASE PREFLIGHT CONTRACT FAILED:', ', '.join(failed))
    raise SystemExit(1)
print(f'RELEASE PREFLIGHT CONTRACT PASSED — {version} — {len(checks)} checks')
