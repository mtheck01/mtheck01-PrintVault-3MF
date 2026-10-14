# PrintVault Engineering Evidence

This log distinguishes observed facts from hypotheses. Update it when a check actually completes; do not infer results from a workflow starting or code existing.

## 2026-10-13 — Initial forensic audit

Repository: [mtheck01/mtheck01-PrintVault-3MF](https://github.com/mtheck01/mtheck01-PrintVault-3MF)  
Main SHA inspected: `f9aa7430a5df24f6183dd0fd803337be101e755f`  
Open PR inspected: [#43](https://github.com/mtheck01/mtheck01-PrintVault-3MF/pull/43)

### Verified workflow evidence

- [Main autonomous run #974](https://github.com/mtheck01/mtheck01-PrintVault-3MF/actions/runs/38481980303) failed after its 2,400-second root-cause analysis limit. The isolated scan discovered and persisted 1,789 records; analysis reported 1,640/1,789 when killed. This confirms the immediate timeout, not the slow file or underlying algorithmic cause.
- [Main autonomous run #973](https://github.com/mtheck01/mtheck01-PrintVault-3MF/actions/runs/38375637173) completed a 1,789-record two-pass analysis: 1,077 resolved, 712 unresolved, 0 conflicts, fingerprint `3C760547A32C81A0BDBFDC9D93CD623BD07FD3B6FC9CB8FB54DEE434466E3BAF`. It is evidence that a full pass can complete, not a current quality baseline.
- The checked-in baseline `automation/baseline-9.0.44.json` describes 1,758 records and has aggregate metrics but no exact inventory or outcome fingerprint. It cannot establish record-level change against the 1,789 scan.
- The runner skipped installer execution because it was non-admin. The earlier launch smoke check observed a process that stayed alive but `MAIN_WINDOW=0`; that does not establish a ready UI.
- PR #43 has passing build/architecture checks at the inspected head, but no measured full-library performance result. Its timeout extension is not proof of a performance repair.
- The likely repeated regex work in semantic evidence fusion is an **unconfirmed hypothesis**. The available logs do not identify a specific slow file or stage.

### Local modularization change

- Local branch: `refactor/category-registry-store`, based on main SHA `f9aa7430a5df24f6183dd0fd803337be101e755f`.
- Added `src/PrintVault.Infrastructure/CategoryRegistryStore.cs`; routed category registry load/save in `MainWindow.xaml.cs` and legacy cleanup/count in `OrganizationService.cs` through this store.
- Intended behavior: retain the current built-in/legacy filtering and organization rules; centralize category registry persistence and recovery.
- Added `tools/PrintVault.Automation/CategoryRegistrySelfTest.cs` for actual load/save/deduplication/removal/failed-save recovery behavior, exposed as `selftest-category-registry`, and wired it into `windows-build.yml`. Added `tools/test_category_registry_store.py` to the autonomous static-source-test list.
- Status: **Partially verified.** `dotnet build src/PrintVault.Infrastructure/PrintVault.Infrastructure.csproj --no-restore` succeeded with 0 errors and one `NU1900` vulnerability-data warning because NuGet's service index was unavailable. The static contract passed. Local restore/build of the automation project failed without a diagnostic error, so the runtime self-test has not run. WPF restore did not produce `src/PrintVault/obj/project.assets.json`, so a complete app build was not established. CI evidence is pending.
- No commit, remote branch, PR, merge or release has been created for this local change.

### Remaining evidence gaps

- The user's production library was not available to this audit; the 1,789 scan refers to the isolated validation folder.
- No exact inventory comparison reconciles 1,758 to 1,789.
- No controlled performance profile identifies the timeout root cause.
- No structured checkpoint report survived the timed-out analysis.
- No admin-capable install or ready-window UI validation was demonstrated.
- No dedicated test project was present in the inspected checkout.
