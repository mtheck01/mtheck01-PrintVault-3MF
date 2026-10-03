from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
fusion = (ROOT / "src/PrintVault.Infrastructure/SemanticEvidenceFusionService.cs").read_text(encoding="utf-8")

required = [
    "storedCategoryIsUnresolved",
    '"Uncategorized"',
    '"Unknown"',
    "!storedCategoryIsUnresolved",
    "var actionableConflict = categoriesDiffer",
]

failed = [x for x in required if x not in fusion]
for x in required:
    print(("PASS: " if x not in failed else "FAIL: ") + x)

if failed:
    print("SEMANTIC UNRESOLVED-CATEGORY CONFLICT GATE 9.0.46 FAILED")
    raise SystemExit(1)

print("SEMANTIC UNRESOLVED-CATEGORY CONFLICT GATE 9.0.46 PASSED")
print("Uncategorized/Unknown are unresolved states, not semantic contradictions.")
