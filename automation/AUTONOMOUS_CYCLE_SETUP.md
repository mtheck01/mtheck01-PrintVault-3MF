# PrintVault Autonomous Validation Runner

The autonomous cycle uses a dedicated Windows self-hosted GitHub Actions runner because the whole-library test must access the real local PrintVault database and model library.

## One-time setup

1. On GitHub, open **Settings → Actions → Runners → New self-hosted runner** for this repository.
2. Choose **Windows x64**.
3. Copy the short-lived runner registration token GitHub provides.
4. On the validation PC, open **PowerShell as Administrator**.
5. Run:

```powershell
Set-ExecutionPolicy Bypass -Scope Process -Force
.\tools\setup_validation_runner.ps1 -RegistrationToken "PASTE-TEMPORARY-TOKEN-HERE"
```

The script installs the runner as a Windows service and assigns the `printvault-validation` label.

## Required repository secret

For autonomous repair, create:

- `OPENAI_API_KEY`

The existing production publishing secret remains:

- `PRINTVAULT_RELEASE_TOKEN`

The repair workflow defaults to `gpt-5.6-sol` and only runs when the whole-library regression gate fails.

## Phone operation

Once the runner is online:

1. Open the PrintVault repository in GitHub.
2. Open **Actions**.
3. Select **PrintVault Autonomous Cycle**.
4. Tap **Run workflow**.
5. Choose the maximum attempts.
6. Leave **Auto repair** enabled.
7. Leave **Publish on pass** enabled.
8. Run it.

GitHub's workflow_dispatch supports manual workflow execution and exposes configured inputs in the Actions UI.

## Safety gates

The cycle will not publish unless:

- source regression tests pass;
- installer builds successfully;
- the installer installs successfully;
- the installed executable reports the expected version;
- PrintVault survives the smoke launch;
- the whole-library runner processes all 1,758 baseline records with zero failures;
- the candidate does not increase the 9.0.44 conflict baseline;
- the candidate materially improves the known lexical/conflict baseline.

The repair agent cannot change VERSION, reset the library, or bypass the regression gate.

If the repair attempt fails, the patch and forensic reports are retained as workflow artifacts instead of being promoted.
