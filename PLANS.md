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

Current classification follow-up: the forensic semantic repair is merged and passed Windows/architecture CI, including installer build and isolated install/launch. Autonomous run #990 completed two passes over the isolated 1,789-file library with zero processing failures, 1,363 applied categories resolved, 426 unresolved, and 111 candidate conflicts. PR #57 repaired the stale category-distribution accounting check. A subsequent policy audit found that the 111 raw candidate disagreements included 28 candidates arbitration intentionally rejected, while 83 were actionable; the workflow nevertheless blocked on every raw disagreement. The workflow policy now uses typed arbitration decisions for blockers, keeps rejected candidates in review evidence, separates deterministic writeback from AI repair, and reports disabled/incomplete repairs accurately. This policy repair is implemented but not verified by CI or a new isolated full-library cycle; details are in `ENGINEERING_EVIDENCE.md`.

426-record semantic coverage repair (2026-10-25): source-title alias coverage, CJK/Latin boundary handling, and distinct language-identification versus actual translation-coverage data are implemented. A replay of the run #992 unresolved fixture produced 85 category candidates; the production arbitration stage applied 82 and retained 344 as Uncategorized. Three candidates remained gated. A regression caught and removed three unsafe generic “stand” promotions, including `Dale Earnhardt 3 Stand Up` and a filename `STORK&STAND` whose embedded title says picture frame. A fresh isolated Deep scan processed 1,789/1,789 files with zero failures and 36 checkpoints. Its cold-start database is not directly comparable to run #992's warm catalog: it showed 82 previously unresolved rows becoming categorized, 13 previously categorized rows becoming Uncategorized, and 154 category changes, but the old catalog's stored semantic fields were absent from the cold-start database. These deltas are not accuracy evidence. CI passed on PR #60, including architecture checks and Windows installer build/install/launch. Classification precision and a true warm-catalog before/after comparison remain unverified; the fixture has no complete human labels. See `ENGINEERING_EVIDENCE.md`.

Run #993 follow-up (2026-10-25): the whole-library analysis completed 1,789/1,789 records with zero per-record failures, but the gate failed because its PowerShell code read `InferredCategory`; the report DTO serializes the field as `CandidateCategory`. Both raw conflict-row accounting and actionable-conflict counting were therefore reading an empty property. A targeted workflow correction now uses the DTO's `CandidateCategory` field in both places. The run's 118 reported conflicts and 0 rows counted by the mismatched property are an accounting-gate defect, not a classification or scan failure. This gate correction is awaiting PR review and a comparable autonomous rerun.

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

Status: **Partially verified; whole-library coverage and outcome preservation are established for the large-model streaming change.** A full Deep scan completed with 0 failures and reconciled all paths. Removing repeated ancestor `XElement.Value` aggregation reduced summed analyzer worker time from 6,556,522 ms to 1,599,177 ms (75.6%) across two local runs; known outlier analysis times fell by roughly 87–94%. Category, semantic type, subtype, unresolved count, and risk flags matched the pre-repair candidate; 46 confidence values decreased by an average of 3.36 percentage points after duplicate geometry text stopped contributing. Follow-up on failed autonomous run #979 traced its timeout to exact component-geometry counting in the forensic trace path; the forensic analyzer now retains semantic tracing while using early-exit geometry probing. On a copy of the 1,789-record test catalog, two complete passes finished in 78.1s and 87.5s with identical fingerprints and 0 failures. Five largest-file exact-vs-fast comparisons matched all classification and structural fields; only the explanatory exact-count wording differs. Self-hosted run [#981](https://github.com/mtheck01/mtheck01-PrintVault-3MF/actions/runs/39348382450) verified the fast forensic path on all 1,789 test records. The process-polling repair was merged in PR #48; self-hosted run [#983](https://github.com/mtheck01/mtheck01-PrintVault-3MF/actions/runs/39359706887) passed in 9m32s versus #982's 33m01s, with matching full-library fingerprints and 0 failures. The source-test section fell from 23m04s to 25.9s. Run #983 trace reports pinpointed large-model XML DOM loading as the remaining analyzer hotspot. PR #49 added streaming for large model XML; autonomous run [#985](https://github.com/mtheck01/mtheck01-PrintVault-3MF/actions/runs/39519460168) on merge SHA `8b7fcd41587fef6d8dd8b64c1b858ddcaf89bc2e` completed in 5m52s. It processed all 1,789 test records, reported 0 failures and 0 conflicts, and reproduced #983's outcome fingerprint. Run #984 on the previous SHA took 7m10s, but one run per SHA does not isolate the parser's causal runtime contribution. A full accuracy review against human labels and a matched repeated-run timing comparison remain outstanding. See `ENGINEERING_EVIDENCE.md`.

## Priority 3 — Verify install, UI readiness and release path

**Goal:** Distinguish build, package, install, launch, full-library validation and publication as separate gates.

Acceptance criteria:

- Run installation on an admin-capable validation environment and retain installer evidence.
- Require a ready main window and meaningful UI smoke flow, not merely a process that remains alive.
- Publish only after the defined quality, coverage, data-integrity and release checks pass; verify the published artifact afterward.

Status: **Partially verified.** Run #985's unconditional UI readiness result was invalid (`MAIN_WINDOW=0` followed by `OPEN_READY=PASS`); PR #50 fixes that reporting defect and adds a regression contract. On PR head `20b4288`, Windows Build run [#661](https://github.com/mtheck01/mtheck01-PrintVault-3MF/actions/runs/39702495140) verified an Administrator token, installed version 9.0.78, and opened a responsive main window using the empty `RUNNER_TEMP\PrintVault-Isolated-Test-Library`; cleanup passed. Architecture Gate run [#486](https://github.com/mtheck01/mtheck01-PrintVault-3MF/actions/runs/39702495137) also passed. This verifies fresh installation and launch on an ephemeral hosted runner. Upgrade behavior, meaningful control interaction, and behavior on the user's machine remain outstanding.

## Priority 4 — Track physical printed inventory

**Goal:** Give the operator an at-a-glance count of finished prints on hand, with variants, storage locations, low-stock thresholds and a traceable adjustment history, without coupling physical stock to file scans or classification.

Acceptance criteria:

- Keep physical stock in independent tables in the existing SQLite database; deleting/rebuilding the digital model catalog must preserve quantities and movement history.
- Support linked or manually named models, multiple variant/location entries, nonnegative counts, reasoned quantity adjustments, low-stock summary, and archive/history views.
- Provide a focused UI that can be exercised against a disposable database; then add verified storage and UI regression coverage.
- Treat filament inventory as a separate follow-on slice with its own units, spool weights and usage semantics.

Status: **Partially verified.** The printed-stock UI and SQLite persistence slice is wired into navigation and the Release solution build succeeds. A disposable four-process lifecycle check verified quantity/history persistence across restart, digital catalog clear and repopulation, model unlinking without accidental relink, and clean SQLite integrity/foreign-key checks. Interactive UI behavior and packaged-installer behavior remain outstanding; filament tracking is not implemented. See `ENGINEERING_EVIDENCE.md`.

## Sequencing and change control

1. Finish and regression-test the category persistence extraction.
2. Establish test infrastructure and exact inventory/outcome evidence.
3. Profile the actual timeout path; repair the measured root cause.
4. Continue modularization in small, tested boundaries.
5. Extend the verified fresh-install/window check with upgrade and meaningful UI-flow validation, then complete release gates.

Do not widen timeouts, weaken gates, merge or publish solely to make a run green.
