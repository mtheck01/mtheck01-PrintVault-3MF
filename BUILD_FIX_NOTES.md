# 9.0.19 Build Fix

Targeted the remaining Semantic Evidence Convergence failure from 9.0.18 (FIX-09 F-16 Fighting Falcon).

The 9.0.18 engine correctly detected identity/domain convergence and raised evidence quality to 85%, but did not assign the broad Aircraft classification when the exact model was absent from the lexical cue table.

9.0.19 now maps any generalized aviation convergence (identity designation + aviation/model descriptor) to `Vehicles / Vehicle / Aircraft` when no named entity has already supplied a more specific classification. This is not an F-16-specific exception.

Conflict safeguards remain unchanged: an existing conflicting stored category keeps REVIEW. Context-only terms remain excluded from identity evidence.

## 9.0.20 — Real-Catalog Numeric-Prefix Entity Recognition

Root cause: `89batmobile_wcad_edition(7)` did not resolve to Batmobile because tokenization produced `89batmobile`, while the entity rule expected the exact token `batmobile`.

Fix: permit a distinctive single-token entity phrase to match when preceded immediately by a numeric SKU/index. Arbitrary substring matching is deliberately not enabled.

Validation target: Semantic + Relationship Integration INT-01 real catalog pair should resolve Batmobile/Batmobile and Same entity.
