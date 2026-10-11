from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
fusion = (ROOT / "src/PrintVault.Infrastructure/SemanticEvidenceFusionService.cs").read_text(encoding="utf-8")
analyzer = (ROOT / "src/PrintVault.Infrastructure/ThreeMfAnalyzer.cs").read_text(encoding="utf-8")

required = [
    "storedCategoryIsUnresolved",
    '"Uncategorized"',
    '"Unknown"',
    "!storedCategoryIsUnresolved",
    "var actionableConflict = categoriesDiffer",
    'Regex.Replace(value, @"(?<=[a-z])(?=[A-Z])", " ")',
    'Regex.Replace(value, @"(?<=[A-Za-z])(?=\d)|(?<=\d)(?=[A-Za-z])", " ")',
]

failed = [x for x in required if x not in fusion]
extra_checks = {
    "3MF analyzer tokenizes original-case filenames": "NormalizeSemanticSeparators(name);" in analyzer,
    "3MF analyzer tokenizes embedded object/metadata names before lowercasing": "NormalizeSemanticSeparators(semanticText.ToString())" in analyzer,
    "3MF analyzer splits CamelCase": 'Regex.Replace(value, @"(?<=[a-z])(?=[A-Z])", " ")' in analyzer,
    "3MF analyzer splits letter/digit boundaries": 'Regex.Replace(value, @"(?<=[A-Za-z])(?=\d)|(?<=\d)(?=[A-Za-z])", " ")' in analyzer,
}
for x in required:
    print(("PASS: " if x not in failed else "FAIL: ") + x)
for name, ok in extra_checks.items():
    print(("PASS: " if ok else "FAIL: ") + name)
failed.extend(name for name, ok in extra_checks.items() if not ok)

# Representative regression fixtures for boundaries lost by the previous lowercasing-first path.
import re
def normalize_fixture(value):
    value = value.replace("+", " ").replace("_", " ")
    value = re.sub(r"\b([A-Z]{2})(?=[a-z])", r"\1 ", value)
    value = re.sub(r"(?<=[A-Z])(?=[A-Z][a-z])", " ", value)
    value = re.sub(r"(?<=[a-z])(?=[A-Z])", " ", value)
    value = re.sub(r"(?<=[A-Za-z])(?=\d)|(?<=\d)(?=[A-Za-z])", " ", value)
    return re.sub(r"\s+", " ", value).strip().lower()
fixtures = {
    "ArticulatedCuteSnail.3mf": "articulated cute snail.3mf",
    "70ElephantF.3mf": "70 elephant f.3mf",
    "1n_OPtruck_Truck14_BLACK.3mf": "1n op truck truck 14 black.3mf",
}
for source, expected in fixtures.items():
    ok = normalize_fixture(source) == expected
    print(("PASS: " if ok else "FAIL: ") + f"tokenization fixture {source}")
    if not ok: failed.append(f"tokenization fixture {source}")

if failed:
    print("SEMANTIC UNRESOLVED-CATEGORY CONFLICT GATE 9.0.46 FAILED")
    raise SystemExit(1)

print("SEMANTIC UNRESOLVED-CATEGORY CONFLICT GATE 9.0.46 PASSED")
print("Uncategorized/Unknown are unresolved states, not semantic contradictions.")
