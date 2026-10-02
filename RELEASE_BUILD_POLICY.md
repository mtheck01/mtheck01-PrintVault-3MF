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
