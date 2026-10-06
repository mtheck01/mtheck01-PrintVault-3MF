# Retired Regression Tests

These release-specific regression scripts were retired from the active `tools/test_*.py` namespace on 2026-10-06.

They asserted obsolete release identities, dead source files, obsolete UI text, or superseded integration paths. Leaving them in the active test namespace would make the repository appear to have current tests that cannot represent the current 9.0.78 architecture.

Retired scripts:

- test_8_6_library_selection.py
- test_entity_classification_integration_8_7_4.py
- test_folder_cleanup.py
- test_hueforge_context_9_0_3.py
- test_hueforge_context_9_0_4.py
- test_independent_evidence_9_0_31.py
- test_library_reset_9_0_29.py
- test_multilingual_entity_8_7_3.py
- test_multilingual_entity_expansion_8_7_5.py
- test_multilingual_metadata_8_7.py
- test_repository_startup_hardening_8_7_1.py
- test_scan_mode_readability.py
- test_semantic_relationship_integration_9_0_9.py
- test_source_derived_analyzer_evidence_9_0_32.py
- test_whole_library_catalog_sync_9_0_33.py
- test_whole_library_root_cause_9_0_30.py

Current policy:

- Historical release tests are not current release gates.
- Current gates must use authoritative versioning rather than embedded obsolete release numbers.
- Tests must not require source files that no longer exist.
- Tests must not assert obsolete UI text or superseded execution paths.
- Current behavior is covered by the active modular architecture, source-integrity, language, semantic-evidence, category-reconciliation, whole-library, build, and release-preflight gates.
