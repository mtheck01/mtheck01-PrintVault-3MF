# PrintVault Engineering Instructions

## Mission

Engineer PrintVault as a reliable, intelligent, self-testing and recoverable 3MF library manager. Preserve useful behavior and existing work. Optimize for correct classification, complete accounting, data integrity and repeatable releases rather than superficial green checks.

## Before changing code

- Read this file, `PLANS.md`, `ENGINEERING_EVIDENCE.md`, relevant source, and the actual workflow logs for the issue.
- Trace failures through the production path and dependencies. Separate confirmed causes from hypotheses and contributing conditions.
- State the existing behavior, defect, intended result, acceptance criteria and regression risks before making a focused change.
- Preserve the user's physical library. Do not move or rewrite production files during development validation.

## Implementation and validation

- Prefer one authoritative intelligence pipeline for production workflows. Do not add parallel or weaker classification paths.
- Keep UI, application orchestration, domain rules and infrastructure responsibilities distinct. Avoid putting persistence, classification or file operations in WPF event handlers.
- Fix root causes. A timeout increase, skipped record, relaxed gate or higher classification count is not a repair by itself.
- Add a regression test for each confirmed defect. Run the affected tests, relevant integration checks, and end-to-end validation required by the acceptance criteria. If a test cannot run, say why and leave the result unverified.
- Account for every eligible record. Distinguish completed, unresolved, skipped and failed outcomes, including the affected file and processing stage.
- For long-running work, preserve completed results and make interruption/recovery behavior explicit.
- Compare classification quality, coverage, data integrity and performance against a trustworthy baseline. Do not force ambiguous files into categories.
- Inspect actual command and workflow results. A build, workflow start, or commit alone does not prove later stages passed.

## Repository and release safety

- Make changes in focused, reviewable groups and preserve unrelated work.
- Do not merge or publish a release automatically. Follow repository release gates and obtain the user's approval where required.
- Do not describe a local change, open PR, workflow start, or partial validation as a merged, released, or complete fix.

## Status language

Use these labels accurately: **Verified**, **Implemented, not verified**, **Partially verified**, **Failed**, **Blocked**, and **Not tested**. Report commit/PR/workflow identifiers, counts, timings and log links when available. Clearly identify missing access to the app, runner, test library or logs.

## Definition of done

A repair is complete only when the root cause is addressed, a regression test passes, required integration/end-to-end checks pass, the original failure is no longer reproduced under comparable conditions, and baseline quality and data integrity have no unexplained regressions. Full-library acceptance additionally requires a reconciled inventory, an outcome for every record, verified database/file consistency, classification analysis and retained diagnostics. Release acceptance also requires installer, install, launch, validation, publication and post-publication evidence.
