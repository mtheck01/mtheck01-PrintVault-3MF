# PrintVault Architecture Upgrade Order

This branch upgrades the existing C#/.NET architecture rather than rewriting PrintVault in another language.

## Order of importance

1. **Publishing safety** — production promotion must be fail-closed. Exhausting repair attempts can never publish a non-PASS candidate.
2. **Runtime foundation** — move the application, Core, Infrastructure, and automation runner to .NET 10.
3. **Library engine boundaries** — keep scanning, evidence, semantic classification, relationships, repair, and reporting as independently testable services.
4. **Deterministic whole-library validation** — the 1,758-record scan remains the release gate and must report reproducible counts.
5. **Repair pipeline** — deterministic repair runs before AI-assisted patch generation; every repair requires a fresh build/install/full-library verification.
6. **Release/deployment contract** — preserve the existing GitHub release, SHA-256 package verification, RemotePatch manifest, version checks, and installed-version health checks.
7. **UI migration/refinement** — change UI components only after the engine and release chain are stable.

## What is deliberately not being replaced

- GitHub repository and release model
- VERSION as the single release authority
- Inno Setup packaging
- RemotePatch manifest and SHA-256 verification
- Windows installation path
- Whole-library database/library
- Existing regression challenges

The goal is to improve the engine without making deployment infrastructure another moving target.

## First foundation change

The application and automation projects target .NET 10, the SQLite provider is aligned to 10.0.0, and both the hosted Windows build and self-hosted validation runner require .NET 10.

## Promotion invariant

A candidate may reach production only after the explicit regression gate returns PASS and the whole-library actionable conflict count is zero. Repair exhaustion is a failure, not a release condition.
