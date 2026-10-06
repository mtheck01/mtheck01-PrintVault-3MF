from __future__ import annotations

import json
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CURRENT_VERSION = (ROOT / "VERSION").read_text(encoding="utf-8").strip()
EXPECTED_REPO = "mtheck01/mtheck01-PrintVault-3MF"
ALLOWED_EXTERNAL_REPOS = {"mtheck01/PrintVault-Updates"}

TEXT_EXTENSIONS = {
    ".py", ".cs", ".csproj", ".xaml", ".xml", ".json", ".md", ".txt", ".ps1",
    ".bat", ".cmd", ".yml", ".yaml", ".props", ".sln", ".iss", ".config"
}
SKIP_DIRS = {".git", "bin", "obj"}

VERSION_RE = re.compile(r"(?<![A-Za-z0-9_])(?:\d+\.\d+\.\d+)(?![A-Za-z0-9_])")
URL_RE = re.compile(r'''https?://[^\s"'<>]+''')
REPO_RE = re.compile(r"(?<![A-Za-z0-9_-])mtheck01/mtheck01-PrintVault-3MF(?![A-Za-z0-9_-])")
LEGACY_REPO_RE = re.compile(r"(?<![A-Za-z0-9_-])mtheck01/PrintVault-3MF(?![A-Za-z0-9_-])")
ANY_GITHUB_REPO_RE = re.compile(r"https?://github\.com/([^/\s]+/[^/\s#?]+)")
TIMEOUT_RE = re.compile(r"(?i)\b(?:timeout(?:-minutes)?|timeoutSeconds)\s*[:=]\s*[0-9]+")
TOOL_REF_RE = re.compile(r"(?:\\|/)?tools[\\/][A-Za-z0-9_.-]+\.(?:py|ps1|csproj|bat)")
ENV_RE = re.compile(r"\$env:([A-Za-z_][A-Za-z0-9_]*)|GetEnvironmentVariable\(\s*['\"]([A-Za-z_][A-Za-z0-9_]*)")

HISTORICAL_TEST_PREFIX = "tools/test_"

def tracked_files():
    p = subprocess.run(["git", "ls-files", "-z"], cwd=ROOT, check=True, capture_output=True)
    return [x.decode("utf-8") for x in p.stdout.split(b"\0") if x]

def read_text(path):
    try:
        raw = path.read_bytes()
    except OSError:
        return None
    if b"\x00" in raw:
        return None
    if path.suffix.lower() not in TEXT_EXTENSIONS and path.name != "VERSION":
        return None
    return raw.decode("utf-8", errors="replace")

def line_number(text, offset):
    return text.count("\n", 0, offset) + 1

def classify_version(path, value, line_text):
    if value == CURRENT_VERSION:
        return "current"
    if path.startswith(HISTORICAL_TEST_PREFIX):
        return "historical-test-fixture"
    if "baseline-" in line_text or "baseline" in line_text.lower():
        return "baseline/validation-fixture"
    if path.startswith("automation/") and value.startswith("9.0."):
        return "baseline/validation-fixture"
    if path.endswith((".md", ".txt")):
        return "historical/documentation"
    return "stale-operational-reference"

def main():
    files = tracked_files()
    findings = []
    failures = []
    text_count = 0

    for rel in files:
        if any(part in SKIP_DIRS for part in Path(rel).parts):
            continue
        text = read_text(ROOT / rel)
        if text is None:
            continue
        text_count += 1

        for match in VERSION_RE.finditer(text):
            value = match.group(0)
            line_text = text.splitlines()[line_number(text, match.start()) - 1]
            kind = classify_version(rel, value, line_text)
            finding = {
                "type": "version", "path": rel,
                "line": line_number(text, match.start()),
                "value": value, "classification": kind
            }
            findings.append(finding)
            if kind == "stale-operational-reference":
                failures.append(finding)

        for match in ANY_GITHUB_REPO_RE.finditer(text):
            repo_value = match.group(1).rstrip(".,);]")
            findings.append({
                "type": "github-repository-url", "path": rel,
                "line": line_number(text, match.start()),
                "value": repo_value,
                "classification": (
                    "current" if repo_value.lower() == EXPECTED_REPO.lower()
                    else "allowed-external" if repo_value.lower() in {x.lower() for x in ALLOWED_EXTERNAL_REPOS}
                    else "repository-mismatch"
                )
            })
            if repo_value.lower() not in {EXPECTED_REPO.lower(), *(x.lower() for x in ALLOWED_EXTERNAL_REPOS)}:
                failures.append({
                    "type": "repository-identity", "path": rel,
                    "line": line_number(text, match.start()),
                    "value": repo_value, "classification": "repository-mismatch"
                })

        for match in LEGACY_REPO_RE.finditer(text):
            failures.append({
                "type": "repository-identity", "path": rel,
                "line": line_number(text, match.start()),
                "value": match.group(0), "classification": "legacy-repository-reference"
            })

        for match in REPO_RE.finditer(text):
            findings.append({
                "type": "repository-identity", "path": rel,
                "line": line_number(text, match.start()),
                "value": match.group(0), "classification": "current"
            })

        for match in URL_RE.finditer(text):
            findings.append({
                "type": "url", "path": rel,
                "line": line_number(text, match.start()),
                "value": match.group(0).rstrip(".,);]")
            })

        for match in TOOL_REF_RE.finditer(text):
            ref = match.group(0).replace("\\", "/").lstrip("./")
            target = ROOT / ref
            classification = "current" if target.exists() else "broken"
            findings.append({
                "type": "tool-reference", "path": rel,
                "line": line_number(text, match.start()),
                "value": ref, "classification": classification
            })
            if not target.exists():
                failures.append({
                    "type": "broken-tool-reference", "path": rel,
                    "line": line_number(text, match.start()),
                    "value": ref, "classification": "broken"
                })

        # Every executable test named by the autonomous workflow must physically exist.
        if rel == ".github/workflows/autonomous-cycle.yml":
            for test_name in re.findall(r'"(test_[A-Za-z0-9_.-]+\.py)"', text):
                test_path = ROOT / "tools" / test_name
                line = line_number(text, text.find(test_name))
                findings.append({
                    "type": "workflow-test-reference", "path": rel,
                    "line": line, "value": f"tools/{test_name}",
                    "classification": "current" if test_path.exists() else "broken"
                })
                if not test_path.exists():
                    failures.append({
                        "type": "broken-test-reference", "path": rel,
                        "line": line, "value": f"tools/{test_name}",
                        "classification": "broken"
                    })

        for match in TIMEOUT_RE.finditer(text):
            findings.append({
                "type": "timeout", "path": rel,
                "line": line_number(text, match.start()),
                "value": match.group(0)
            })

        for match in ENV_RE.finditer(text):
            findings.append({
                "type": "environment-variable", "path": rel,
                "line": line_number(text, match.start()),
                "value": match.group(1) or match.group(2)
            })

    workflow_path = ".github/workflows/autonomous-cycle.yml"
    workflow = ROOT / workflow_path
    if not workflow.exists():
        failures.append({"type": "critical-chain", "value": workflow_path, "classification": "missing"})
    else:
        w = workflow.read_text(encoding="utf-8")
        required = [
            "cancel-in-progress: false",
            "queue: max",
            'python ".\\tools\\forensic_repository_audit.py"',
            "MODULE_PASS=$pass/2",
            "MODULE_LOCKS=CREATED",
            "Invoke-WholeLibrary -Pass 1",
            "Invoke-WholeLibrary -Pass 2",
            "WHOLE_LIBRARY_TWO_PASS=PASS",
            "PROMOTION_GUARD_START_SHA=",
            "Promotion blocked: origin/main changed during validation.",
            "Install-Candidate",
            "Start-FinalApp",
            "Close-PrintVault",
            "LEGACY_TAXONOMY_CLEANUP",
            "FINAL_APP_SMOKE",
            "Publish-WholeLibraryValidationStatus",
            "Publish-SourceRelease",
            "Upload autonomous diagnostics"
        ]
        for item in required:
            if item not in w:
                failures.append({
                    "type": "critical-chain", "path": workflow_path,
                    "value": item, "classification": "missing-invariant"
                })

    contract = ROOT / "tools/test_autonomous_cycle_contract.py"
    if not contract.exists():
        failures.append({"type": "critical-chain", "value": str(contract), "classification": "missing"})

    stale = [x for x in failures if x.get("type") == "version"]
    broken = [x for x in failures if x.get("type") == "broken-tool-reference"]
    critical = [x for x in failures if x.get("type") == "critical-chain"]

    report = {
        "version": CURRENT_VERSION,
        "repository": EXPECTED_REPO,
        "tracked_files": len(files),
        "text_files_scanned": text_count,
        "findings": findings,
        "failure_count": len(failures),
        "stale_operational_version_count": len(stale),
        "broken_tool_reference_count": len(broken),
        "critical_chain_failure_count": len(critical),
        "failures": failures,
        "result": "PASS" if not failures else "FAIL"
    }

    out_dir = ROOT / "build_logs"
    out_dir.mkdir(exist_ok=True)
    (out_dir / "forensic-repository-audit.json").write_text(
        json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8"
    )

    print(f"FORENSIC_REPOSITORY_FILES={len(files)}")
    print(f"FORENSIC_TEXT_FILES_SCANNED={text_count}")
    print(f"FORENSIC_VERSION={CURRENT_VERSION}")
    print(f"FORENSIC_STALE_OPERATIONAL_VERSIONS={len(stale)}")
    print(f"FORENSIC_BROKEN_TOOL_REFERENCES={len(broken)}")
    print(f"FORENSIC_CRITICAL_CHAIN_FAILURES={len(critical)}")
    print(f"FORENSIC_RESULT={report['result']}")

    if failures:
        for item in failures[:100]:
            print("FORENSIC_FAILURE=" + json.dumps(item, ensure_ascii=False))
        return 1
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
