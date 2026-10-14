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

Status: **Not established.** See evidence log. Current timeout cause is confirmed as the immediate run failure; the underlying performance cause is not confirmed.

## Priority 1 — Modularize without changing classification semantics

**Goal:** Move cohesive responsibilities out of WPF event handlers and make behavior independently testable.

Acceptance criteria:

- Extract category-registry persistence behind an Infrastructure boundary used by both the UI and organization workflow.
- Add regression tests for load, save, duplicate handling, legacy cleanup, corrupt-file recovery and injected storage paths.
- Move library lifecycle/orchestration and view state out of `MainWindow` in small slices, preserving the existing authoritative intelligence pipeline.
- Every extraction has a passing regression test and existing build/architecture checks.

Status: **Partially implemented, not verified at the app level.** A local category-registry extraction and runtime self-test exist on `refactor/category-registry-store`. The static contract passes and Infrastructure builds; the runtime self-test and WPF app build could not complete locally. CI evidence is pending.

## Priority 2 — Diagnose and repair whole-library performance

**Goal:** Identify the measured bottleneck and improve runtime without reducing coverage or classification quality.

Acceptance criteria:

- Profile the same inventory and runner at the baseline SHA and candidate SHA.
- Measure the time and invocation count of parsing, source analysis, semantic fusion, persistence and other identified hot stages.
- Compare the candidate's complete outcome fingerprint and per-record results with the baseline; explain every changed outcome.
- Complete the full library within a justified operating budget, with resumable/preserved results after interruption.

Status: **Root cause unconfirmed.** Regex-heavy semantic matching is a hypothesis that needs a controlled profile/A-B measurement.

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
