import json
import os
import sys
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]
REPORT = Path(sys.argv[1]) if len(sys.argv) > 1 else None
MODEL = os.environ.get("OPENAI_REPAIR_MODEL", "gpt-5.6-sol")
if not REPORT or not REPORT.exists():
    raise SystemExit("Usage: autonomous_repair.py <report.json>")

api_key = os.environ.get("OPENAI_API_KEY")
if not api_key:
    raise SystemExit("OPENAI_API_KEY is required for autonomous repair.")

report = json.loads(REPORT.read_text(encoding="utf-8"))
if report.get("Failures", report.get("failed", 0)) not in (0, None):
    raise SystemExit("Refusing repair from a failed analysis run.")

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
Return ONLY JSON with keys summary and patch. patch must be a unified diff that
applies with git apply. Keep the patch minimal and production-safe. Add or update
a regression test when appropriate. Do not modify VERSION; the orchestrator owns it.

Current whole-library report:
""" + json.dumps(report, indent=2) + """

Relevant source:
""" + json.dumps(source, indent=2)

payload = {
    "model": MODEL,
    "input": [
        {"role": "system", "content": "Return strict JSON only."},
        {"role": "user", "content": prompt},
    ],
}

print(f"REPAIR_MODEL={MODEL}")
print("REPAIR_API=RESPONSES")
print("REPAIR_REQUEST=START")
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

print("REPAIR_REQUEST=COMPLETE")
text = data.get("output_text")
if not text:
    chunks = []
    for item in data.get("output", []):
        for content in item.get("content", []):
            if content.get("type") == "output_text":
                chunks.append(content.get("text", ""))
    text = "".join(chunks)

if not text:
    raise SystemExit("Repair model returned no output.")

try:
    result = json.loads(text)
except json.JSONDecodeError as exc:
    (ROOT / "build_logs" / "autonomous-repair-raw.txt").write_text(text, encoding="utf-8")
    raise SystemExit(f"Repair model did not return valid JSON: {exc}")

patch = result.get("patch", "")
if not patch.strip():
    raise SystemExit("Repair model returned an empty patch.")

patch_path = ROOT / "build_logs" / "autonomous-repair.patch"
patch_path.parent.mkdir(parents=True, exist_ok=True)
patch_path.write_text(patch, encoding="utf-8")
(ROOT / "build_logs" / "autonomous-repair-summary.json").write_text(
    json.dumps({"model": MODEL, "summary": result.get("summary", "")}, indent=2),
    encoding="utf-8",
)
print(f"PATCH={patch_path}")
print(f"SUMMARY={result.get('summary','')}")
