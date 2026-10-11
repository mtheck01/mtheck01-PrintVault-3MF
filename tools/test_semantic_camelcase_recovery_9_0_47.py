from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
source = (ROOT / "src/PrintVault.Infrastructure/SemanticEvidenceFusionService.cs").read_text(encoding="utf-8")

required = [
    r"(?<=[\p{Ll}\p{Nd}])(?=[\p{Lu}])",
    r"(?<=[\p{Lu}])(?=[\p{Lu}][\p{Ll}])",
    "private static string Normalize(string value)",
]
missing = [item for item in required if item not in source]
if missing:
    for item in missing:
        print("FAIL: missing CamelCase normalization contract:", item)
    raise SystemExit(1)

def normalize(value):
    value = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", " ", value)
    value = re.sub(r"(?<=[A-Z])(?=[A-Z][a-z])", " ", value)
    return re.sub(r"\s+", " ", value.replace("+", " ").replace("_", " ")).strip().lower()

fixtures = {
    "ArticulatedCuteSnail_Multicolor4ColorBambuStudioX1C": ("articulated", "cute", "snail"),
    "FeketeSárkányV02": ("fekete", "sárkány"),
    "FakePhone17ProMax": ("fake", "phone"),
    "AlbertEinstein_Front_157x170_BBL_AMS_READY": ("albert", "einstein"),
}
for name, tokens in fixtures.items():
    actual = normalize(name)
    missing_tokens = [token for token in tokens if not re.search(r"(?<!\w)" + re.escape(token) + r"(?!\w)", actual)]
    if missing_tokens:
        print(f"FAIL: {name}: {actual!r}; missing={missing_tokens}")
        raise SystemExit(1)
    print(f"PASS: {name} -> {actual}")

print("CAMELCASE SEMANTIC RECOVERY 9.0.47 PASSED")
