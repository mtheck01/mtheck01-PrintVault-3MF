# PrintVault Release Build Policy

## Version authority
`VERSION` at the repository root is the single authoritative release version.

The solution-wide `Directory.Build.props` reads that file and supplies Version, AssemblyVersion, FileVersion, and InformationalVersion to every project.

The Windows build script reads the same `VERSION` file. It does not contain a manually maintained release number.

The installer receives the release version from the build script through the Inno Setup preprocessor (`/DMyAppVersion=...`).

The runtime `AppVersion` reads the assembly version rather than carrying another hard-coded release number.

## Required release preflight
`build_windows.bat` must stop before restore if any active release metadata is inconsistent. It validates:

- VERSION exists and contains one semantic version.
- Project does not contain its own hard-coded `<Version>`.
- Directory.Build.props provides the solution-wide version.
- Runtime version derives from assembly metadata.
- Installer accepts its version from the build environment.
- Main window derives its displayed version from AppVersion.

Historical challenge/report text is allowed to retain its historical build number. It is not release metadata.

## Packaging rule
A release package must contain one source root only. Do not nest an extracted source package inside another source package. The Windows build log must identify the exact source root.


## Canonical production release pipeline
There is one production release path: `.github/workflows/windows-build.yml`.

- Every push to `main` runs the complete Windows build and test gate.
- A production release is eligible only when the commit changes `VERSION`.
- The release publisher consumes the installer produced by that same successful build; it does not rebuild a second source snapshot.
- The publisher refuses an existing release version and refuses to move the RemotePatch manifest backward or sideways.
- The package is locally extracted and verified before publication.
- After publication, the exact GitHub release asset is downloaded and its SHA-256 is compared with the locally built package.
- Only after that remote verification succeeds is `manifest.json` promoted.
- The manifest is fetched again after promotion and its version, URL, and SHA-256 are verified.
- Any failure before manifest promotion leaves the previous RemotePatch manifest unchanged.
- The former standalone `release.yml` workflow must not be recreated; a second release path would bypass the canonical gate.

This makes the release chain fail closed:

`source commit -> complete build/tests -> tested installer -> package -> publish -> download/verify -> manifest promotion -> final manifest verification`.

A CI-only commit that does not change `VERSION` cannot publish a production release.
