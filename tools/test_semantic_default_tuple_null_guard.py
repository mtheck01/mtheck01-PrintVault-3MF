from pathlib import Path

SOURCE = Path("src/PrintVault.Infrastructure/SemanticEvidenceFusionService.cs").read_text(encoding="utf-8")

GUARD = "if (best.Hits is null || best.Hits.Length == 0 || best.Cue.Terms is null) return false;"
assert GUARD in SOURCE, "Contextual entity cue guard must handle default tuple null arrays before dereferencing them."

method_start = SOURCE.index("private static bool IsContextualEntityWithArtifactCue(")
method_end = SOURCE.index("\n    private static string InferType", method_start)
method = SOURCE[method_start:method_end]
guard_position = method.index(GUARD)
assert method.index("best.Hits.Length") > guard_position, "Hits.Length must only be evaluated after the null guard."
print("PASS: default semantic cue tuple null guard is present and ordered safely.")
