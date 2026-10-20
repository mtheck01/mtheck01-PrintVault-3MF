# PrintVault Engineering Roadmap

This roadmap records verified progress; it is not a list of completed work based on intent or workflow starts. See `ENGINEERING_EVIDENCE.md` for the current evidence and limitations.

## Current architecture

- `PrintVault.Core` contains shared models and library contracts.
- `PrintVault.Infrastructure` contains SQLite persistence, file discovery, analysis, categorization, organization and diagnostics.
- `PrintVault` is the WPF application. `MainWindow.xaml.cs` currently combines UI state, library lifecycle, category handling and numerous workflows; it is a major modularization target.
- The solution currently has no dedicated automated test project. Existing workflow/static checks do not replace focused unit and integration regression tests.

## Priority 0 — Establish trustworthy validation

**Goal:** Make whole-library results reproducible and auditable before treating speed or classification changes as improvements.

Acceptance criteria:

- Produce a stable inventory fingerprint for the isolated validation library and reconcile it with the checked-in 1,758-record baseline and current 1,789-record scan.
- Persist per-record terminal outcomes and stage diagnostics, including unresolved, skipped and failed records; an interrupted pass must not appear complete.
- Define a comparable baseline for classification outcomes and data integrity, not only aggregate counts.
- Run the same inventory on a known code SHA and runner; record elapsed time and per-stage/per-file timing.
- Preserve all source 3MF files during validation and verify catalog/file consistency.

Status: **Partially implemented, full-library acceptance not established.** Scan checkpoints preserve completed records every 50 files and stage timing is emitted. Two candidate Deep scans each completed 1,789/1,789 paths with zero failures and reconciled inventory fingerprint `F6AB7CE6BADCB0CEFE53955D7917090422B1ADF3AE2683C2C4952CE5FFFAA33D`. The later performance repair preserved category, semantic type, subtype, unresolved count, and risk-flag counts across the complete candidate; 46 confidence values decreased after duplicated geometry text was removed from a non-semantic scoring input. Historic reconciliation with the 1,758-record checked-in baseline and reproducible clean source SHA remain outstanding. The latest 75-row review is a selected diagnostic sample, not library-wide ground truth; 17 Deep-vs-human mismatches still need focused diagnosis. See `ENGINEERING_EVIDENCE.md` and `outputs/deep-scan-summary.md`.

## Priority 1 — Modularize without changing classification semantics

**Goal:** Move cohesive responsibilities out of WPF event handlers and make behavior independently testable.

Acceptance criteria:

- Extract category-registry persistence behind an Infrastructure boundary used by both the UI and organization workflow.
- Add regression tests for load, save, duplicate handling, legacy cleanup, corrupt-file recovery and injected storage paths.
- Move library lifecycle/orchestration and view state out of `MainWindow` in small slices, preserving the existing authoritative intelligence pipeline.
- Every extraction has a passing regression test and existing build/architecture checks.

Status: **Partially verified.** The category-registry extraction and runtime self-test pass locally; the full WPF solution builds. Interactive UI behavior, installer validation, and CI evidence remain outstanding.

## Priority 2 — Diagnose and repair whole-library performance

**Goal:** Identify the measured bottleneck and improve runtime without reducing coverage or classification quality.

Acceptance criteria:

- Profile the same inventory and runner at the baseline SHA and candidate SHA.
- Measure the time and invocation count of parsing, source analysis, semantic fusion, persistence and other identified hot stages.
- Compare the candidate's complete outcome fingerprint and per-record results with the baseline; explain every changed outcome.
- Complete the full library within a justified operating budget, with resumable/preserved results after interruption.

Status: **Partially verified; two speed improvements now have measured evidence, while the current large-model parser candidate still needs a full-cycle run.** A full Deep scan completed with 0 failures and reconciled all paths. Removing repeated ancestor `XElement.Value` aggregation reduced summed analyzer worker time from 6,556,522 ms to 1,599,177 ms (75.6%) across two local runs; known outlier analysis times fell by roughly 87–94%. Category, semantic type, subtype, unresolved count, and risk flags matched the pre-repair candidate; 46 confidence values decreased by an average of 3.36 percentage points after duplicate geometry text stopped contributing. Follow-up on failed autonomous run #979 traced its timeout to exact component-geometry counting in the forensic trace path; the forensic analyzer now retains semantic tracing while using early-exit geometry probing. On a copy of the 1,789-record test catalog, two complete passes finished in 78.1s and 87.5s with identical fingerprints and 0 failures. Five largest-file exact-vs-fast comparisons matched all classification and structural fields; only the explanatory exact-count wording differs. Self-hosted run [#981](https://github.com/mtheck01/mtheck01-PrintVault-3MF/actions/runs/39348382450) verified the fast forensic path on all 1,789 test records. The process-polling repair was merged in PR #48; self-hosted run [#983](https://github.com/mtheck01/mtheck01-PrintVault-3MF/actions/runs/39359706887) passed in 9m32s versus #982's 33m01s, with matching full-library fingerprints and 0 failures. The source-test section fell from 23m04s to 25.9s. Run #983 trace reports then pinpointed large-model XML DOM loading as the remaining analyzer hotspot. A local streaming-reader candidate matched all analyzer output fields against #983 for five large files and materially reduced their individual parse times, but its full-library fingerprint and workflow performance remain unverified. A matched baseline SHA/runner comparison and full accuracy review against human labels remain outstanding. See `ENGINEERING_EVIDENCE.md`.

## Priority 3 — Verify install, UI readiness and release path

**Goal:** Distinguish build, package, install, launch, full-library validation and publication as separate gates.

Acceptance criteria:

- Run installation on an admin-capable validation environment and retain installer evidence.
- Require a ready main window and meaningful UI smoke flow, not merely a process that remains alive.
- Publish only after the defined quality, coverage, data-integrity and release checks pass; verify the published artifact afterward.

Status: **Partially verified.** The current self-hosted runner skipped installer execution because it is non-admin. Earlier process-liveness validation did not establish a visible main window.

## Sequencing and change control

1. Finish and regression-test the category persistence extraction.
2. Establish test infrastructure and exact inventory/outcome evidence.
3. Profile the actual timeout path; repair the measured root cause.
4. Continue modularization in small, tested boundaries.
5. Complete admin-capable installation/UI validation and release gates.

Do not widen timeouts, weaken gates, merge or publish solely to make a run green.
