from pathlib import Path
import importlib.util

ROOT = Path(__file__).resolve().parents[1]
AUDIT = ROOT / "tools" / "forensic_repository_audit.py"
spec = importlib.util.spec_from_file_location("forensic_repository_audit", AUDIT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

cases = [
    (".github/workflows/windows-build.yml", "9.0.47", "      - name: Test CamelCase semantic recovery 9.0.47", "historical-test-fixture"),
    (".github/workflows/windows-build.yml", "9.0.78", "          $version = (Get-Content \".\\VERSION\" -Raw).Trim()", "current"),
    (".github/workflows/windows-build.yml", "9.0.47", "          $version = \"9.0.47\"", "stale-operational-reference"),
]
for path, value, line, expected in cases:
    actual = module.classify_version(path, value, line)
    if actual != expected:
        raise SystemExit(f"FAIL: {path} {value} -> {actual}; expected {expected}")
    print(f"PASS: {value} classified as {actual}")
print("FORENSIC VERSION CLASSIFICATION REGRESSION PASSED")
