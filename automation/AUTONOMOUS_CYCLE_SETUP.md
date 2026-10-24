# PrintVault Autonomous Validation Runner

The autonomous cycle uses a dedicated Windows self-hosted GitHub Actions runner because the whole-library test must access the isolated validation database and the dedicated local test model library.

## One-time setup

1. On GitHub, open **Settings → Actions → Runners → New self-hosted runner** for this repository.
2. Choose **Windows x64**.
3. Copy the short-lived runner registration token GitHub provides.
4. On the validation PC, open **PowerShell as Administrator**.
5. Run the registration script with `-InstallService` only if you want the runner to operate as a Windows service:

```powershell
Set-ExecutionPolicy Bypass -Scope Process -Force
.\tools\setup_validation_runner.ps1 -RegistrationToken "PASTE-TEMPORARY-TOKEN-HERE" -InstallService
```

The script assigns the `printvault-validation` label. When installed as a service, the workflow selects a dedicated `PrintVault-Test\library.db` under that service account's profile; it must never use the normal `PrintVault\library.db`. The service account needs read/write access to `D:\3d print files test` and permission to create its isolated test database. Do not configure the runner to use the production database.

## Required repository secret

For optional AI source-code repair, create:

- `OPENAI_API_KEY`

The optional production publishing secret is:

- `PRINTVAULT_RELEASE_TOKEN`

AI repair is off by default and uses API credits only when explicitly enabled. Deterministic category writeback uses the isolated test database and does not require the OpenAI key. Publishing is off by default.

## Phone operation

Once the runner is online:

1. Open the PrintVault repository in GitHub.
2. Open **Actions**.
3. Select **PrintVault Autonomous Cycle**.
4. Tap **Run workflow**.
5. Choose the maximum attempts. The workflow raises this to the minimum safe count if the selected repair modes need more passes.
6. Enable **Auto repair** only when you want an AI-generated source patch and accept the API cost.
7. Keep **Publish on pass** off unless you intentionally want to publish a validated release.
8. Run it and inspect the streamed stage and process logs or download its diagnostics artifact.

GitHub's workflow_dispatch supports manual workflow execution and exposes configured inputs in the Actions UI.

## Safety gates

The cycle will not publish unless:

- source regression tests pass;
- installer builds successfully;
- the installer installs successfully;
- the installed executable reports the expected version;
- PrintVault survives the smoke launch;
- the whole-library runner processes the complete discovered test catalog with zero failures;
- all writebacks have an explicit `ACTIONABLE` arbitration decision and the report matches the current catalog;
- two validation passes reproduce the same per-record outcome fingerprint.

The repair agent cannot change VERSION, reset the library, or bypass the regression gate.

If any stage fails, the effective settings, failure stage, process output and available forensic reports are retained as workflow artifacts instead of being promoted.
