## 9.0.33 — Whole-Library Catalog Synchronization Repair

The 9.0.32 run exposed an infrastructure-level failure: the dashboard showed 1,758 rebuilt records while the whole-library forensic service independently read a zero-record repository and reported a false clean pass. 9.0.33 makes the forensic workflow use the active catalog snapshot, blocks overlap with scans/rebuilds, and repairs an empty repository from that exact populated snapshot before analysis. The report explicitly records the synchronization state so a zero-record forensic result cannot silently masquerade as a successful whole-library analysis.

# PrintVault 3MF 9.0.16 Build Notes

## Objective
Move from entity coverage measurement to evidence fusion: combine independent evidence conservatively without inventing named entities or automatically changing classifications.

## Engine
- SemanticEvidenceFusionService is deterministic and read-only.
- Named entities remain strongest identity evidence.
- Translation/analyzer/stored classification provide corroboration.
- Lexical domain cues can improve classification confidence but do not create named entities.
- HueForge size and printer/device terms are context-only.
- Review is required when evidence is incomplete or insufficiently corroborated.

## Validation
- Semantic Evidence Fusion Challenge added to Engineering/Test Labs.
- Full-library audit, controlled fixtures, 48 deterministic parity samples, production fingerprint protection, and physical 3MF protection.
- Windows/WPF compilation must be verified on Windows; Linux environment cannot authoritatively compile WPF.

## 9.0.17 build target
Build this package on Windows before running the challenge. Expected project version is 9.0.17.
The change is confined to SemanticEvidenceFusionService and its challenge expectations, plus version/test metadata. No production classification writes, physical-file changes, or relationship-engine changes are introduced.

## Release pipeline hardening (9.0.16 FIX2)
The Windows build now runs `tools/release_preflight.ps1` before restore/build. The validator reports each version/source check independently and fails closed with an exact diagnostic instead of an opaque version-preflight error. `VERSION` remains the single authoritative release version.


### FIX3 release-preflight hardening
The release preflight reads VERSION directly; the batch wrapper no longer passes ExpectedVersion as a required PowerShell parameter. Preflight output is captured and echoed to the console and build log so any failed check is visible.

## 9.0.16 FIX4
- Fixed release preflight path handling.
- Removed `GetFullPath` canonicalization from the PowerShell validator because the FIX3 build demonstrated that it can throw `Illegal characters in path` when invoked from the Windows batch environment.
- The validator now consumes the already-absolute batch root directly and reports the actual check results.
- Build script prints the exact preflight root and script path before invocation.


## 9.0.16 — Semantic Object/Reference Roles
Targeted semantic fix: role-aware organizer detection prevents referenced tool names such as "wrench" from becoming the primary identity of a tool organizer. Controlled regression expects Tools & Workshop / Organizer / Tool Organizer for socket tray wrench organizer, while preserving the referenced tool evidence.

## 9.0.32 — Whole-Library Evidence Expansion

The 9.0.31 analyzer cleanup sharply reduced lexical false disagreement, but the forensic pass then exposed a much larger `INSUFFICIENT_INDEPENDENT_EVIDENCE` population. 9.0.32 addresses that population by allowing the rebuilt analyzer's explicit, source-derived semantic result to serve as a bounded evidence channel.

The analyzer result is accepted only when:
- `IntelligenceReason` contains an explicit built-in category prefix;
- `SemanticType` is present;
- `IntelligenceScore` is at least 0.35; and
- the category is one of PrintVault's built-in categories.

This is deliberately not lexical feedback. `Tags`, `SuggestedTags`, the fusion result, and stored classification remain excluded from the lexical evidence corpus. Conflicting stored categories continue to produce REVIEW.

Validation performed in the source package:
- Independent evidence test — PASS
- Source-derived analyzer evidence test — PASS
- Whole-library root-cause contract — PASS
- Semantic taxonomy contract — PASS
- Library reset contract — PASS
- Static audit — PASS
- Release preflight contract — PASS

The package has not been Windows/.NET compiled in this environment because the .NET SDK is unavailable here.
