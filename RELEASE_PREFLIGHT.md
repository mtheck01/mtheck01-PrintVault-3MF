# Release Preflight Policy

PrintVault 3MF releases use `VERSION` at the package root as the single authoritative release version.

`build_windows.bat` delegates version validation to `tools/release_preflight.ps1` before restore, build, publish, or installer creation.

The validator reports each check individually and fails closed. It verifies:

- VERSION exists, is semantic `x.y.z`, and matches the requested release.
- Directory.Build.props derives Version, AssemblyVersion, FileVersion, and InformationalVersion from VERSION.
- The application project does not hard-code a Version value.
- AppVersion derives from compiled assembly metadata.
- MainWindow displays AppVersion.
- Inno Setup consumes the version passed by the build script.
- The solution exists.
- The package does not contain a nested duplicate source root.

A preflight failure must identify the exact failed check in `build_logs/build_output.log`.
