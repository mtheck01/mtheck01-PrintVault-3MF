import json
import os
import sys
import subprocess
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]
REPORT = Path(sys.argv[1]) if len(sys.argv) > 1 else None
SOURCE_FAILURES = Path(sys.argv[2]) if len(sys.argv) > 2 else None
MODEL = os.environ.get("OPENAI_REPAIR_MODEL", "gpt-5.6-sol")
if not REPORT or not REPORT.exists():
    raise SystemExit("Usage: autonomous_repair.py <report.json> [source-test-failures.json]")

api_key = os.environ.get("OPENAI_API_KEY")
if not api_key:
    raise SystemExit("OPENAI_API_KEY is required for autonomous repair.")

report = json.loads(REPORT.read_text(encoding="utf-8"))
if report.get("Failures", report.get("failed", 0)) not in (0, None):
    raise SystemExit("Refusing repair from a failed analysis run.")

source_failures = []
if SOURCE_FAILURES and SOURCE_FAILURES.exists():
    try:
        source_failures = json.loads(SOURCE_FAILURES.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise SystemExit(f"Invalid source-test failure report: {exc}")
if not isinstance(source_failures, list):
    raise SystemExit("Source-test failure report must contain a JSON array.")

paths = [
    "src/PrintVault.Infrastructure/SemanticEvidenceFusionService.cs",
    "src/PrintVault.Infrastructure/EntityClassificationService.cs",
    "src/PrintVault.Infrastructure/WholeLibraryRootCauseAnalysisService.cs",
    "src/PrintVault.Infrastructure/ThreeMfAnalyzer.cs",
    "src/PrintVault.Infrastructure/LibraryEngine.cs",
    "tools/test_semantic_evidence_dimensional_arbitration_9_0_44.py",
]
source = {}
for rel in paths:
    p = ROOT / rel
    if p.exists():
        source[rel] = p.read_text(encoding="utf-8", errors="replace")[:60000]

prompt = """You are the autonomous repair engineer for PrintVault 3MF.
Repair only a demonstrated semantic regression. Do not reset/delete the library.
Do not weaken tests, lower thresholds merely to make counts look better, or change
stored classifications directly. Preserve the dimensional architecture:
subject identity, artifact/function, and catalog category are separate.
Return ONLY JSON with keys summary and edits. edits must be an array of objects with
"path", "old", and "new" keys. Each edit must replace an EXACT, UNIQUE text block in
an existing file. The repair runner will generate the git patch itself, so do NOT
return a unified diff, Markdown fences, or Begin/End Patch markers. If no edit is
needed, return an empty edits array. Keep edits minimal and production-safe. Add or
update a regression test when appropriate. Do not modify VERSION; the orchestrator owns it.

Current whole-library report:
""" + json.dumps(report, indent=2) + """

Independent source-test failures recorded before the whole-library diagnostic:
""" + json.dumps(source_failures, indent=2) + """

Relevant source:
""" + json.dumps(source, indent=2)

def extract_output_text(data):
    text = data.get("output_text")
    if text:
        return text
    chunks = []
    for item in data.get("output", []):
        for content in item.get("content", []):
            if content.get("type") == "output_text":
                chunks.append(content.get("text", ""))
    return "".join(chunks)


def apply_edits(result):
    edits = result.get("edits")
    if not isinstance(edits, list):
        raise ValueError("Repair model must return an 'edits' array.")

    allowed = set(paths)
    originals = {}
    changed = []

    for index, edit in enumerate(edits, 1):
        if not isinstance(edit, dict):
            raise ValueError(f"Edit {index} must be an object.")
        rel = edit.get("path")
        old = edit.get("old")
        new = edit.get("new")
        if not isinstance(rel, str) or rel not in allowed:
            raise ValueError(f"Edit {index} targets a disallowed or invalid path: {rel!r}")
        if not isinstance(old, str) or not isinstance(new, str):
            raise ValueError(f"Edit {index} must contain string old/new values.")
        p = ROOT / rel
        if not p.exists():
            raise ValueError(f"Edit {index} targets missing file: {rel}")
        if rel not in originals:
            originals[rel] = p.read_text(encoding="utf-8", errors="replace")
        current = p.read_text(encoding="utf-8", errors="replace")
        occurrences = current.count(old)
        if occurrences != 1:
            raise ValueError(
                f"Edit {index} requires exactly one match in {rel}; found {occurrences}."
            )
        p.write_text(current.replace(old, new, 1), encoding="utf-8")
        if rel not in changed:
            changed.append(rel)

    if not changed:
        raise ValueError("Repair model returned no edits.")

    diff = subprocess.run(
        ["git", "diff", "--", *changed],
        cwd=ROOT, text=True, capture_output=True
    )
    if diff.returncode != 0:
        for rel, original in originals.items():
            (ROOT / rel).write_text(original, encoding="utf-8")
        raise ValueError("git diff failed while generating the repair patch.")

    patch = diff.stdout
    if not patch.startswith("diff --git "):
        for rel, original in originals.items():
            (ROOT / rel).write_text(original, encoding="utf-8")
        raise ValueError("Generated repair patch is empty or malformed.")

    patch_path = ROOT / "build_logs" / "autonomous-repair.patch"
    patch_path.parent.mkdir(parents=True, exist_ok=True)
    patch_path.write_text(patch, encoding="utf-8")

    check = subprocess.run(
        ["git", "apply", "--check", str(patch_path)],
        cwd=ROOT, text=True, capture_output=True
    )
    if check.returncode != 0:
        error = (check.stdout + check.stderr).strip() or "unknown patch validation error"
        for rel, original in originals.items():
            (ROOT / rel).write_text(original, encoding="utf-8")
        raise ValueError(f"Generated patch failed git apply --check: {error}")

    (ROOT / "build_logs" / "autonomous-repair-summary.json").write_text(
        json.dumps({
            "model": MODEL,
            "summary": result.get("summary", ""),
            "edited_paths": changed,
            "edit_count": len(edits),
        }, indent=2),
        encoding="utf-8",
    )
    print(f"PATCH={patch_path}")
    print(f"SUMMARY={result.get('summary','')}")
    print("PATCH_VALIDATION=PASS")


def request_repair(request_prompt):
    payload = {
        "model": MODEL,
        "input": [
            {"role": "system", "content": "Return strict JSON only. Never return Markdown."},
            {"role": "user", "content": request_prompt},
        ],
    }
    req = Request(
        "https://api.openai.com/v1/responses",
        data=json.dumps(payload).encode("utf-8"),
        headers={"Authorization": f"Bearer {api_key}", "Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urlopen(req, timeout=300) as response:
            data = json.load(response)
    except HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        print(f"REPAIR_API_HTTP_ERROR={exc.code}", file=sys.stderr)
        print(body[:12000], file=sys.stderr)
        raise SystemExit(f"OpenAI Responses API returned HTTP {exc.code}.")
    except URLError as exc:
        print(f"REPAIR_API_NETWORK_ERROR={exc}", file=sys.stderr)
        raise SystemExit("OpenAI Responses API network request failed.")

    text = extract_output_text(data)
    if not text:
        raise ValueError("Repair model returned no output.")
    try:
        result = json.loads(text)
    except json.JSONDecodeError as exc:
        (ROOT / "build_logs" / "autonomous-repair-raw.txt").write_text(text, encoding="utf-8")
        raise ValueError(f"Repair model did not return valid JSON: {exc}")
    return result


print(f"REPAIR_MODEL={MODEL}")
print("REPAIR_API=RESPONSES")
print("REPAIR_REQUEST=START")

last_error = ""
for repair_attempt in range(1, 4):
    print(f"REPAIR_ATTEMPT={repair_attempt}/3")
    retry_prompt = prompt
    if last_error:
        retry_prompt += (
            "\n\nThe previous candidate edit set was rejected by the local validator. "
            "Do not repeat it. Correct the exact old/new text blocks and return a complete "
            "replacement JSON response. The exact validator error was:\n" + last_error
        )
    try:
        result = request_repair(retry_prompt)
        apply_edits(result)
        break
    except (ValueError, KeyError) as exc:
        last_error = str(exc)
        print(f"PATCH_RESPONSE=INVALID ERROR={last_error}", file=sys.stderr)
else:
    raise SystemExit(
        "Repair model failed to produce a valid source edit set after 3 validated attempts. "
        + last_error
    )

print("REPAIR_REQUEST=COMPLETE")
