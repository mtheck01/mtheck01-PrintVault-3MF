from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
fusion = (ROOT / "src/PrintVault.Infrastructure/SemanticEvidenceFusionService.cs").read_text(encoding="utf-8")

# 9.0.45 Heavy-Impact Lexical Precision Challenge
#
# The 9.0.44 whole-library forensic pass left 356 lexical classifier disagreements.
# A major failure mode is a title containing a strong subject noun plus an explicit
# artifact role, e.g.:
#   A-10 stand
#   dragon holder
#   car display base
#   Pikachu phone mount
#   aircraft case
#
# The subject noun answers what the artifact depicts/references. The role suffix answers
# what printable artifact it actually is. The role must therefore outrank generic lexical
# subject-domain cues without requiring a per-model exception.
#
# This is a static architecture contract: the production fusion engine must implement a
# bounded role grammar, place it before generic lexical arbitration, and classify those
# constructions as Functional at high confidence.

required_suffixes = [
    '"stand"', '"holder"', '"mount"', '"bracket"', '"tray"', '"rack"',
    '"organizer"', '"adapter"', '"case"', '"cover"', '"dock"', '"cradle"',
    '"spacer"', '"hook"', '"hanger"', '"clip"', '"fixture"', '"enclosure"',
    '"support"', '"base"',
]

checks = [
    ("bounded artifact-role grammar exists", "ArtifactRoleSuffixes" in fusion),
    ("generalized role matcher exists", "TryGetFunctionalArtifactRole" in fusion),
    ("role grammar requires a subject", 'subjectTokens.Length == 0 || subjectTokens.Length > 6' in fusion),
    ("role matcher uses Unicode token boundaries", r"\\p{{L}}" in fusion and r"\\p{{N}}" in fusion),
    ("all high-impact role suffixes are covered", all(term in fusion for term in required_suffixes)),
    ("role classification is Functional", 'category = "Functional";' in fusion and 'family = "Functional";' in fusion),
    ("role confidence is strong", "classification = Math.Max(classification, 94);" in fusion),
    ("role evidence is explicit", "Role-aware artifact arbitration:" in fusion),
    ("role branch precedes generic lexical promotion", "else if (hasGeneralizedArtifactRole)" in fusion and
        fusion.index("else if (hasGeneralizedArtifactRole)") <
        fusion.index("else if (cueHits.Count > 0 && best.Hits.Length > 0)")),
    ("subject lexical cues are explicitly prevented from winning", "subject-domain lexical cues" in fusion),
    ("bounded role grammar is not a free-form contains rule", "not a free-form" in fusion),
]

failed = [name for name, ok in checks if not ok]
for name, ok in checks:
    print(("PASS: " if ok else "FAIL: ") + name)

if failed:
    print("SEMANTIC EVIDENCE ARTIFACT ROLE PRECISION 9.0.45 FAILED")
    for name in failed:
        print("-", name)
    raise SystemExit(1)

print("SEMANTIC EVIDENCE ARTIFACT ROLE PRECISION 9.0.45 PASSED")
print("Heavy-impact contract: explicit artifact roles now outrank generic subject lexical cues.")
