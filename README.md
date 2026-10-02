# 9.0.22 — Whole-Library Entity Coverage V1

See `WHOLE_LIBRARY_ENTITY_COVERAGE_V1.md` for the coverage fix and verification scope.

# PrintVault 3MF 9.0.17

PrintVault is a native Windows 3MF library manager focused on safe organization and model intelligence.

## 9.0.9 — Semantic + Relationship Integration
- Adds an integration gate between named-entity semantic intelligence and relationship reasoning.
- Uses independently specified expected entities and expected relationship behavior.
- Covers same-entity aliases, multilingual identity, HueForge size variants, same-size different-entity negatives, shared-printer negatives, category/domain similarity, and generic vocabulary.
- Real catalog pairs are opportunistic; unavailable real pairs are not failures.
- Controlled fixtures provide deterministic regression coverage when the library lacks a specific model.
- Production data and physical 3MF files remain read-only during the challenge.

## Safety
Tests use SQLite snapshots. They do not rename, move, overwrite, or modify physical 3MF files.

## Build
Run `build_windows.bat` on Windows with the .NET 8 SDK and Inno Setup installed. A Windows build log is authoritative for compilation status.


## 9.0.12 Test Lab
The active 9.x regression surface is Entity Semantic Regression, HueForge Adversarial Regression, Relationship Identity Gate, Semantic + Relationship Integration, and Full Library Intelligence Challenge. Superseded one-off challenge windows were removed from the active UI.
