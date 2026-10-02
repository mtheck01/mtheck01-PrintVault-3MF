# 9.0.33 — Whole-Library Catalog Synchronization Repair

- Root-cause analysis now waits for active scans/rebuilds before starting.
- The analysis receives the exact active UI catalog snapshot instead of independently re-querying an unverified catalog state.
- If the repository unexpectedly contains 0 records while the active catalog contains valid records, the repository is synchronized from that exact catalog snapshot before analysis.
- Reports and JSON now record repository count before/after synchronization, active snapshot count, data source, and mismatch state.
- The forensic pass no longer produces a misleading Catalog 0 / PASS result when the dashboard contains a populated catalog.
- No 3MF files are modified and no semantic classification is invented during synchronization.

# 9.0.32 — Large-Scale Source-Derived Analyzer Evidence Fix

- Targets the whole-library `INSUFFICIENT_INDEPENDENT_EVIDENCE` population rather than a single model fixture.
- Reuses the 9.0.31 clean analyzer result as a bounded semantic evidence channel when the analyzer emitted an explicit built-in category, semantic type, and sufficient intelligence score.
- Raises evidence quality from clean source-derived analyzer semantics without feeding `Tags`, `SuggestedTags`, `IntelligenceReason`, or stored classification back into lexical matching.
- Preserves conflict review when source-derived analyzer semantics disagree with a stored built-in/manual classification.
- Leaves named-entity authority, aviation convergence, taxonomy handling, library reset behavior, and physical 3MF files unchanged.
- Adds dedicated static validation for source-derived analyzer evidence.

# 9.0.22 — Whole-Library Entity Coverage V1

- Adds precision-first named-entity anchors for high-value catalog filenames that were previously left unrecognized.
- Covers distinctive aviation identifiers, military vehicles, Star Wars/fictional entities, characters, and known model families.
- Keeps generic vocabulary out of the entity layer; broad words such as `aircraft`, `truck`, `car`, and `horse` are not promoted to identity.
- Preserves numeric-prefix, printer/device, HueForge size, dimension, color, and variant suffix handling as context rather than identity.
- Adds aliases for known catalog spellings such as `Hoghwarts`, `TIE_ADVANCED-XPA`, and `Zelda+Mastersword`.
- Raises whole-library coverage without changing production records or physical 3MF files.

# 9.0.20 — Semantic Relationship Integration Fix

- Generalized named-entity recognition for real catalog filenames that prepend numeric SKUs/indices directly to a distinctive single-token entity name (for example `89batmobile_wcad_edition`).
- Preserved exact-token matching and avoided arbitrary substring matching so generic vocabulary cannot create false identities.
- Updated Semantic + Relationship Integration Challenge reporting to BUILD 9.0.20.
- Target regression: real-catalog INT-01 Batmobile separate-file pair.

# PrintVault 3MF 9.0.19

- Fixed generalized aviation convergence classification when a known aircraft designation and model/domain descriptor are present but the lexical cue table has no exact model entry (e.g. F-16 Fighting Falcon).
- The rule assigns the broad `Vehicles / Vehicle / Aircraft` classification only when both identity-bearing aviation designation and independent aviation/model-domain evidence converge.
- Preserved conflicting stored-category REVIEW behavior and context-only safeguards.
- Semantic Evidence Convergence remains read-only and deterministic.

## 9.0.18 — Generalized Semantic Evidence Convergence Fix v2

- Strengthened the aviation evidence convergence gate for any identity-bearing aircraft designation combined with independent aviation-domain terminology.
- Raises converged evidence quality to the strong-evidence band without special-casing A-10.
- Strong convergence clears REVIEW only when there is no conflicting stored category.
- Preserves context-only isolation and existing named-entity authority.
- Corrected FIX-07 so its stored-category conflict is a real adversarial fixture instead of sharing FIX-01's clean fixture.
- Added F-16 and AH-64 controlled cases to verify the fix generalizes beyond A-10.
- Preserved read-only audit, deterministic parity, catalog fingerprint, and physical-file protections.

## 9.0.16 — Semantic Object/Reference Role Fix

- Added role-aware tool organizer detection.
- Prevented referenced tools (for example Wrench) from becoming the primary model identity when the model is a holder, rack, tray, or organizer.
- Added explicit referenced-tool evidence.
- Updated FIX-06 regression to expect Tools & Workshop / Organizer / Tool Organizer.
- Preserved read-only evidence fusion and conservative review behavior.

## 9.0.16 — Semantic Evidence Convergence

- Added a single authoritative VERSION file and solution-wide version propagation.
- Hardened the Windows release preflight so version drift fails before compilation.
- Installer version is supplied by the build pipeline instead of duplicated source metadata.
- Runtime version derives from assembly metadata.
- Added release build policy and packaging safeguards.

## 9.0.13 — Semantic Evidence Fusion
- Added deterministic SemanticEvidenceFusionService.
- Combines named-entity, translation, analyzer, stored classification, lexical and context evidence without mutating records.
- Context-only signals such as HueForge size and printer metadata cannot establish identity.
- Added full-library Semantic Evidence Fusion Challenge with independent controlled expectations, deterministic parity, catalog fingerprint protection, and physical-file protection.

# PrintVault 3MF Changelog

## 9.0.12
- Added Semantic Coverage & Confidence Challenge for full-library read-only semantic coverage auditing.
- Separates high/medium/low/no-entity coverage, semantic contradiction flags, and identity coverage opportunities.
- Added deterministic semantic parity and production/physical-file safety checks.
- Hardened named-entity phrase matching to use token boundaries for Latin text, preventing substrings such as `car` from matching `card`.
- Preserved 9.0.11 selective relationship indexing and permanent regression suite.

# PrintVault 3MF Changelog

## 9.0.9 — Semantic + Relationship Integration
- Added Semantic + Relationship Integration Challenge.
- Independently validates entity identity before relationship behavior.
- Added controlled regression fixtures for A-4 Skyhawk/A4 Blue Angels, Colonial Viper, Pikachu, DeLorean, Batmobile, Crane, Truck, and adversarial context cases.
- Missing real-library pairs are UNAVAILABLE rather than failures.
- Added Engineering/Test Labs navigation entry.

## 9.0.8 — Entity Semantic Regression V2
- Added real-catalog versus controlled-fixture separation.
- Added independent A-4 Skyhawk, A4 Blue Angels, and TIE Fighter semantic fixtures.
- Unavailable real cases no longer count as semantic failures.

## 9.0.6 — Relationship Identity Gate & Precision
- Replaced near-exhaustive relationship candidate generation with indexed identity-anchor retrieval.
- Added relationship precision challenge and adversarial context controls.

## 9.0.11 — Test Lab Consolidation + Full Library Intelligence
- Consolidated the 9.x Engineering/Test Lab surface around permanent regression gates.
- Removed superseded Smart Category Context, Entity Domain, Semantic Intelligence Deep, and 100-model Batch challenge windows/services from the active build.
- Added Full Library Intelligence Challenge for controlled all-record validation, candidate-index performance, determinism, catalog fingerprint safety, and physical-file protection.
- Preserved Entity Semantic Regression, HueForge Adversarial, Relationship Identity Gate, and Semantic + Relationship Integration as maintained regression suites.

## 9.0.16 — Semantic Evidence Convergence
- Replaced single-source evidence scoring with conservative evidence convergence.
- Added identity-bearing model/designation and domain terminology as separate lexical evidence dimensions.
- Analyzer, stored classification, translation, named entity, and lexical evidence now reinforce one another.
- Context-only signals remain excluded from identity/classification confidence.
- Lexical inference that conflicts with a stored category remains REVIEW.
- Expanded controlled challenge cases to test convergence and conflict behavior.
- Read-only audit behavior and physical-file protection remain unchanged.

## 9.0.29 — Semantic Evidence Test Contract Fix

- Fixed Semantic Evidence Convergence controlled-case taxonomy comparisons.
- Added EXACT / DESCENDANT / MISMATCH reporting.
- Expected taxonomy paths now represent minimum required paths for controlled evidence cases.
- Kept review/conflict semantics unchanged.
- No entity recognition, classifier, production catalog, or physical 3MF mutation logic changed.
- Added static regression audit for the corrected contract.
