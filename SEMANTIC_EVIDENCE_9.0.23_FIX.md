# PrintVault 3MF 9.0.24 — Semantic Evidence Test Contract Fix

## Purpose

Correct the **Semantic Evidence Convergence challenge contract** that was rejecting valid, more-specific taxonomy results from the 9.0.22 intelligence engine.

This is a **test-contract correction**, not a classifier change.

## What changed

The challenge now evaluates classification as:

- **EXACT** — expected taxonomy path and actual taxonomy path are identical.
- **DESCENDANT** — the actual taxonomy path starts with the expected minimum path and then becomes more specific.
- **MISMATCH** — category/type differ, or the actual taxonomy path is not under the expected minimum path.

Examples:

- Expected `Vehicles / Vehicle / Aircraft` + actual `Vehicles / Vehicle / Aircraft / Attack Aircraft` → **DESCENDANT / PASS**
- Expected `Vehicles / Vehicle / Aircraft` + actual `Vehicles / Vehicle / Aircraft / Transport Aircraft` → **DESCENDANT / PASS**
- Expected `Vehicles / Vehicle / Aircraft / Attack Aircraft` + actual same path → **EXACT / PASS**
- Expected `Vehicles / Vehicle / Aircraft` + actual `Vehicles / Vehicle / Watercraft / Aircraft Carrier` → **MISMATCH / FAIL** because `Watercraft` is not under the expected `Aircraft` path.

The test does **not** infer taxonomy from words. In particular, `Aircraft Carrier` is not treated as an aircraft merely because its name contains “Aircraft.”

## Controlled cases corrected

- FIX-01 A-10 → minimum `Aircraft`
- FIX-02 A400M → minimum `Aircraft`
- FIX-03 A-4 → exact `Aircraft / Attack Aircraft`
- FIX-07 conflicting A-10 → minimum `Aircraft`; review expectation remains independent
- FIX-09 F-16 → minimum `Aircraft`
- FIX-10 AH-64 → minimum `Aircraft`

Review/conflict behavior is unchanged.

## Files changed

- `src/PrintVault.Infrastructure/SemanticEvidenceFusionChallengeService.cs`
- `VERSION` → `9.0.24`
- `tools/test_semantic_evidence_taxonomy_contract_9_0_23.py`
- this documentation file

## Validation performed here

Static source validation passed.

The existing version-consistency audit also passed for all 7 challenge services with dynamic build/report versioning.

The Semantic+Relationship version-safe static audit passed.

A Windows/.NET compilation was **not performed in this environment**; run the normal Windows build script before installing the resulting application.
