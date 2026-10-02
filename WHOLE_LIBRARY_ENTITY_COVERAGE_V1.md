# 9.0.22 — Whole-Library Entity Coverage V1

This fix expands deterministic named-entity recognition for high-value catalog filenames that were visible in the 9.0.20 Full Library Intelligence audit but remained `Entity=(none)`.

## Design rules

- Identity anchors must be distinctive model/entity identifiers.
- Generic words such as `aircraft`, `truck`, `car`, and `horse` remain contextual vocabulary rather than new identities.
- Numeric SKU/index prefixes may precede a single-token identity anchor, using the existing narrow numeric-prefix rule.
- HueForge dimensions, printer/device names, colors, multipart markers, and version suffixes remain context/variant metadata.
- No production catalog or physical 3MF file is modified by the recognition layer.

## Coverage anchors added

Aviation: A-10 Thunderbolt II, Airbus A400M, AH-64 Apache, AH-6 Little Bird, MH-6 Little Bird, AV-8B Harrier, F-16 Fighting Falcon, P-51 Mustang, PBY Catalina, P-38 Lightning, T-6 Texan, Boeing 747, Airbus A380, Airbus A318, Antonov An-225, F-111 Aardvark, Supermarine Spitfire, Hawker Sea Fury.

Other high-value catalog identities: M3A3 Bradley, TIE Advanced, Wall-E, Wile E. Coyote, Pac-Man, Hogwarts spelling variant, Zelda Master Sword, Luffy, Nami, Sanji, and Cinderwing3D Tiny Horse.

## Verification

Static coverage-anchor regression, representative filename normalization checks, challenge-version consistency, semantic/relationship static audit, XAML/C# integrity, UI feature audit, and semantic intelligence audit are run before packaging.

A Windows .NET compile is still required on the user's Windows build environment because this Linux container does not have the .NET SDK installed.
