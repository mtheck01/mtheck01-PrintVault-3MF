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

Status: **Partially implemented, full-library acceptance not established.** Scan checkpoints preserve completed records every 50 files and stage timing is emitted. Two candidate Deep scans each completed 1,789/1,789 paths with zero failures and reconciled inventory fingerprint `F6AB7CE6BADCB0CEFE53955D7917090422B1ADF3AE2683C2C4952CE5FFFAA33D`. Autonomous run #997 then completed two passes over 1,789 records on merged SHA `5447416`, with 0 processing failures and identical outcome fingerprint `115FF116158657EC29DB5F32F9D3338B5EA1AD38C056C2E3A069F404A829862D`; it reported 1,443 resolved, 346 unresolved, 36 rejected/protected review conflicts, and 0 actionable conflicts. This establishes repeat-pass consistency, not classification accuracy. The 1,758-record historical baseline remains informational and unreconciled. The latest 75-row review is a selected diagnostic sample, not library-wide ground truth; 17 Deep-vs-human mismatches still need focused diagnosis. See `ENGINEERING_EVIDENCE.md` and `outputs/deep-scan-summary.md`.

Current classification follow-up: the forensic semantic repair is merged and passed Windows/architecture CI, including installer build and isolated install/launch. Autonomous run #990 completed two passes over the isolated 1,789-file library with zero processing failures, 1,363 applied categories resolved, 426 unresolved, and 111 candidate conflicts. PR #57 repaired the stale category-distribution accounting check. A subsequent policy audit found that the 111 raw candidate disagreements included 28 candidates arbitration intentionally rejected, while 83 were actionable; the workflow nevertheless blocked on every raw disagreement. The workflow policy now uses typed arbitration decisions for blockers, keeps rejected candidates in review evidence, separates deterministic writeback from AI repair, and reports disabled/incomplete repairs accurately. This policy repair is implemented but not verified by CI or a new isolated full-library cycle; details are in `ENGINEERING_EVIDENCE.md`.

426-record semantic coverage repair (2026-10-25): source-title alias coverage, CJK/Latin boundary handling, and distinct language-identification versus actual translation-coverage data are implemented. A replay of the run #992 unresolved fixture produced 85 category candidates; the production arbitration stage applied 82 and retained 344 as Uncategorized. Three candidates remained gated. A regression caught and removed three unsafe generic “stand” promotions, including `Dale Earnhardt 3 Stand Up` and a filename `STORK&STAND` whose embedded title says picture frame. A fresh isolated Deep scan processed 1,789/1,789 files with zero failures and 36 checkpoints. Its cold-start database is not directly comparable to run #992's warm catalog: it showed 82 previously unresolved rows becoming categorized, 13 previously categorized rows becoming Uncategorized, and 154 category changes, but the old catalog's stored semantic fields were absent from the cold-start database. These deltas are not accuracy evidence. CI passed on PR #60, including architecture checks and Windows installer build/install/launch. Classification precision and a true warm-catalog before/after comparison remain unverified; the fixture has no complete human labels. See `ENGINEERING_EVIDENCE.md`.

Autonomous repair handoff (2026-10-26): run #995 used merged SHA `33a1677` and passed the corrected PowerShell conflict accounting, but failed before backup/writeback because the C# repair command searched the report's camelCase `catalog`/`processed` envelope as case-sensitive PascalCase `Catalog`/`Processed`. The same command also requested `InferredCategory` from rows that expose `CandidateCategory`; this second mismatch would have made a casing-only fix a no-op. A typed, case-insensitive report contract and executable round-trip regression self-test are implemented on the current repair branch. Automation build and targeted contracts pass locally. A full autonomous rerun on this repair is still required; no classification or repair writeback is yet verified.

Run #997 unresolved follow-up (2026-10-28): source-title evidence is absent from the lexical cue corpus for 247 of 279 low-quality unresolved rows; 28 have no primary source title, and 4 have lexical matches that still fail to yield a candidate. A locally replayed fix for letter/digit cue normalization and six narrowly scoped title patterns produced 8 candidates from 343 unresolved records while preserving negative controls. Root-cause reporting now separates primary-title/no-rule-match from absent evidence. This is **Implemented, locally verified** only: full 1,789-record autonomous validation, classification precision across the corpus, CI and writeback remain unverified. See the latest entry in `ENGINEERING_EVIDENCE.md`.

Unresolved coverage follow-up: prepared the representative 20-record human-label batch under `outputs/semantic-review/`; see the completed labels and replay result below. Continue using title size, printer settings, folder name, and subject identity as context only unless they establish the printed artifact's form.

Human-label follow-up (2026-10-30): user labeled 17 of the 20 records; 3 “not found” files remain uncategorized. Implemented title/form cues with nearby negative controls. Replay against the 343 unresolved rows from run #997 now yields 25 candidates and 318 still unresolved; the earlier eight candidates remain, and 17 newly labeled rows are promoted. A broad Death Star cue was narrowed after it matched a separate 200x200 HueForge tile. Semantic self-test, all 48 Python source checks, and the Release solution build pass locally. CI, a fresh full-library scan, and writeback are outstanding.

Classification follow-up (2026-11-11): added typed source-analyzer disagreement reporting and fail-closed review arbitration, while preserving explicit artifact-role precedence. Removed confidence “convergence” credit for translation/analyzer results derived from the same title lineage, and narrowed an incidental photo-frame description match. Semantic self-test 56/56, all 48 Python checks, and Release build pass locally. The 343-row historical replay remains at 30 candidates / 313 unresolved and contains zero analyzer-channel conflicts; it is not a fresh full-library scan or precision evaluation. Isolated scan and human review of changed outcomes are required before broad use. See `ENGINEERING_EVIDENCE.md`.

Population analysis of the 318 residual rows: 290 retain primary embedded title/name evidence, while 315 receive no match from current lexical cues. The user confirmed that category-named fixture folders mix manual placements with early app classifications, so folder paths are contaminated and excluded as ground truth. The 50 description-bearing residual records are a better candidate for mining explicit artifact-form phrases with positive and negative tests; the 42 Hueforge/200x200 files are an accuracy-review cohort only. Do not promote dimensions, printer settings, subject names, paths, or generic strings to categories. Full counts, limitations and next acceptance criteria are in `ENGINEERING_EVIDENCE.md`.

Deep engineering audit confirmed that descriptions are intentionally extracted and diagnostic-matched, but excluded from entity/fusion decision text. The targeted repair now adds a separate typed, bounded description-role path for explicit lightbox, picture-frame, refrigerator-magnet, memorial-plaque and grid-wall-peg constructions; it preserves provenance, rejects cross-category description conflicts, and defers to explicit primary title/name roles without relaxing global gates. Local semantic self-test (including a synthetic `.3mf` extraction-to-pipeline check), all 48 Python source checks, Release solution build and `git diff --check` pass. Replay of the 343 historical unresolved rows yields 30 candidates, five more than the saved pre-repair replay, with no prior candidate removed. A fresh Deep scan of the available isolated test folder completed 1,786/1,786 with zero failures; read-only root-cause accounting also covered all 1,786 records with zero failures and 324 unresolved. The folder differs by three files from the historical 1,789-record fixture, so exact inventory reconciliation to that baseline remains outstanding. The five exact promoted archives were separately analyzed through production extraction and inference and produced the intended role categories. Human precision adjudication, CI, and PR/release remain outstanding; see `ENGINEERING_EVIDENCE.md`.

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

Status: **Partially verified.** PR #63 fixes the false `FINAL_APP_SMOKE_CLOSED=PASS` result: process cleanup and UI readiness are now reported separately, session 0 is explicitly `NOT_VERIFIED`, and publication is blocked without a responsive interactive UI smoke. PR #63 merged as `5447416943011a1ee2f0bd807348c5e059a70fd0`; post-merge Architecture Gate #517 and Windows Build #692 passed. Windows Build #692 built the installer and passed isolated install-and-launch on a hosted admin runner. Autonomous run #997 then confirmed the session-0 reporting and publication block end to end while completing two consistent library passes. This verifies fresh install/launch and accurate session-0 reporting, but not upgrade behavior, meaningful control interaction, or UI readiness on the self-hosted runner or the user's machine. Run #997's library checks passed, but the custom commit status remains pending and no release was published. See `ENGINEERING_EVIDENCE.md`.

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

Current UI-smoke follow-up (2026-10-28): PR #63 corrected the autonomous workflow's `FINAL_APP_SMOKE_CLOSED=PASS` mislabel. Run [#997](https://github.com/mtheck01/mtheck01-PrintVault-3MF/actions/runs/40627058442) verified the full autonomous workflow end to end: it reports `FINAL_APP_UI_SMOKE=NOT_VERIFIED REASON=NON_INTERACTIVE_SESSION_0 SESSION_ID=0`, separately records successful process cleanup, and sets `PUBLISH=DISABLED`. The workflow run concluded success because the two-pass library validation succeeded, but the custom `printvault/autonomous-cycle` commit status correctly remains pending until UI readiness is verified. See `ENGINEERING_EVIDENCE.md` for the full scan metrics and remaining acceptance gaps.
