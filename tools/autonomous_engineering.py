import json
import os
import re
import subprocess
import sys
from pathlib import Path
from urllib.request import Request, urlopen
from urllib.error import HTTPError

ROOT = Path(__file__).resolve().parents[1]
MODEL = os.environ.get("OPENAI_REPAIR_MODEL", "gpt-5.6-sol")
TOKEN = os.environ.get("GITHUB_TOKEN")
REPO = os.environ.get("GITHUB_REPOSITORY", "mtheck01/mtheck01-PrintVault-3MF")
ISSUE_PREFIX = "[PrintVault-Autonomous]"
MAX_CONTEXT = 160000
ALLOWED_ROOTS = ("src/", "tools/", "tests/", "automation/")
BLOCKED_NAMES = {"VERSION", "build_logs"}

if not TOKEN:
    raise SystemExit("GITHUB_TOKEN is required.")
if REPO != "mtheck01/mtheck01-PrintVault-3MF":
    raise SystemExit(f"Repository identity mismatch: {REPO}")
API = "https://api.github.com"

def api(method, path, payload=None):
    data = None if payload is None else json.dumps(payload).encode("utf-8")
    req = Request(
        API + path,
        data=data,
        headers={
            "Authorization": f"Bearer {TOKEN}",
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2026-03-10",
            "User-Agent": "PrintVault-Autonomous-Engineering-Agent",
            "Content-Type": "application/json",
        },
        method=method,
    )
    try:
        with urlopen(req, timeout=60) as r:
            return json.load(r)
    except HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"GitHub API {method} {path} -> HTTP {exc.code}: {body[:4000]}")

def find_issue():
    q = f'repo:{REPO} is:issue is:open in:title "{ISSUE_PREFIX}"'
    result = api("GET", "/search/issues?q=" + __import__("urllib.parse").parse.quote(q))
    issues = result.get("items", [])
    issues.sort(key=lambda x: x.get("created_at", ""))
    return issues[0] if issues else None

def git_files():
    p = subprocess.run(["git", "ls-files"], cwd=ROOT, text=True, capture_output=True, check=True)
    return [x for x in p.stdout.splitlines() if x]

def select_context(request):
    words = re.findall(r"[A-Za-z][A-Za-z0-9_.-]{4,}", request.lower())
    stop = {
        "printvault","autonomous","please","should","could","would","there","their",
        "about","after","before","again","problem","issue","fix","make","need",
        "have","with","from","that","this","when","where","into","does","will",
        "been","being","then","than","only","also","very","must","what","your"
    }
    terms = []
    for w in words:
        if w not in stop and w not in terms:
            terms.append(w)
    terms = terms[:14]

    files = git_files()
    selected = []
    for f in files:
        if not f.startswith(ALLOWED_ROOTS):
            continue
        if any(part in BLOCKED_NAMES for part in Path(f).parts):
            continue
        if f.endswith((".py", ".cs", ".csproj", ".json", ".xml", ".xaml", ".md", ".ps1", ".bat")):
            selected.append(f)

    ranked = []
    pattern = "|".join(re.escape(t) for t in terms)
    if pattern:
        for root in ("src", "tools", "tests", "automation"):
            if not Path(ROOT / root).exists():
                continue
            p = subprocess.run(
                ["git", "grep", "-Il", "-i", "-E", pattern, "--", root],
                cwd=ROOT, text=True, capture_output=True
            )
            ranked.extend(p.stdout.splitlines())

    # Prefer files matched by the request, then a bounded set of architecture files.
    ordered = []
    for f in ranked + selected:
        if f not in ordered:
            ordered.append(f)

    context = {}
    total = 0
    for f in ordered:
        p = ROOT / f
        try:
            text = p.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        if len(text) > 30000:
            text = text[:30000]
        if total + len(text) > MAX_CONTEXT:
            continue
        context[f] = text
        total += len(text)
        if total >= MAX_CONTEXT:
            break
    return context, terms

def extract_text(data):
    if data.get("output_text"):
        return data["output_text"]
    out = []
    for item in data.get("output", []):
        for c in item.get("content", []):
            if c.get("type") == "output_text":
                out.append(c.get("text", ""))
    return "".join(out)

def request_model(prompt):
    payload = {
        "model": MODEL,
        "input": [
            {"role": "system", "content": "Return strict JSON only. Never return Markdown."},
            {"role": "user", "content": prompt},
        ],
    }
    req = Request(
        "https://api.openai.com/v1/responses",
        data=json.dumps(payload).encode("utf-8"),
        headers={"Authorization": "Bearer " + os.environ["OPENAI_API_KEY"],
                 "Content-Type": "application/json"},
        method="POST",
    )

    # A 429 is a transport/rate-limit failure, not a model-validation failure.
    # Retry it here with bounded exponential backoff so the outer engineering
    # attempt budget is not burned by three immediate identical requests.
    last_429 = None
    for transport_attempt in range(1, 5):
        try:
            with urlopen(req, timeout=300) as r:
                data = json.load(r)
            text = extract_text(data)
            if not text:
                raise ValueError("Model returned no output.")
            return json.loads(text)
        except HTTPError as exc:
            body = exc.read().decode("utf-8", errors="replace")
            if exc.code != 429:
                raise RuntimeError(f"OpenAI API HTTP {exc.code}: {body[:4000]}")
            retry_after = exc.headers.get("Retry-After")
            try:
                delay = float(retry_after) if retry_after else float(2 ** (transport_attempt - 1))
            except (TypeError, ValueError):
                delay = float(2 ** (transport_attempt - 1))
            delay = max(1.0, min(delay, 60.0))
            last_429 = f"OpenAI API HTTP 429: {body[:4000]}"
            print(
                f"ENGINEERING_MODEL_RATE_LIMIT attempt={transport_attempt}/4 "
                f"retry_after={retry_after!r} sleep={delay:.1f}s",
                file=sys.stderr,
            )
            if transport_attempt < 4:
                import time
                time.sleep(delay)
    raise RuntimeError(last_429 or "OpenAI API HTTP 429: rate limit persisted after retries.")

def apply_edits(result):
    action = result.get("action")
    edits = result.get("edits")
    if action not in ("patch", "validate"):
        raise ValueError("Model must return action=patch or action=validate.")
    if not isinstance(edits, list):
        raise ValueError("Model must return an edits array.")
    if action == "validate" and edits:
        raise ValueError("Validate-only jobs must not modify source.")
    if action == "validate":
        summary = result.get("summary", "")
        if not summary or any(x in summary.lower() for x in ("cannot", "unable", "insufficient", "not enough context")):
            raise ValueError("Validate-only result must contain a concrete successful rationale.")
        (ROOT / "build_logs").mkdir(exist_ok=True)
        (ROOT / "build_logs" / "autonomous-engineering-job.json").write_text(
            json.dumps({"issue": ISSUE["number"], "title": ISSUE["title"],
                        "model": MODEL, "action": "validate",
                        "edited_paths": [], "edit_count": 0,
                        "summary": summary}, indent=2), encoding="utf-8")
        return [], summary
    originals = {}
    changed = []
    for i, e in enumerate(edits, 1):
        path, old, new = e.get("path"), e.get("old"), e.get("new")
        if not isinstance(path, str) or not path.startswith(ALLOWED_ROOTS):
            raise ValueError(f"Edit {i} targets a disallowed path: {path!r}")
        if Path(path).name in BLOCKED_NAMES or "build_logs" in Path(path).parts:
            raise ValueError(f"Edit {i} targets a blocked path: {path}")
        if not isinstance(old, str) or not isinstance(new, str):
            raise ValueError(f"Edit {i} must contain string old/new values.")
        p = ROOT / path
        if not p.exists():
            raise ValueError(f"Missing edit target: {path}")
        current = p.read_text(encoding="utf-8", errors="replace")
        if current.count(old) != 1:
            raise ValueError(f"Edit {i} requires exactly one match in {path}; found {current.count(old)}.")
        if path not in originals:
            originals[path] = current
        p.write_text(current.replace(old, new, 1), encoding="utf-8")
        if path not in changed:
            changed.append(path)

    diff = subprocess.run(["git", "diff", "--", *changed], cwd=ROOT, text=True,
                          capture_output=True)
    if diff.returncode != 0 or not diff.stdout.startswith("diff --git "):
        for path, original in originals.items():
            (ROOT / path).write_text(original, encoding="utf-8")
        raise ValueError("Generated patch is empty or malformed.")

    patch = ROOT / "build_logs" / "autonomous-engineering.patch"
    patch.parent.mkdir(exist_ok=True)
    patch.write_text(diff.stdout, encoding="utf-8")

    for path, original in originals.items():
        (ROOT / path).write_text(original, encoding="utf-8")

    check = subprocess.run(["git", "apply", "--check", str(patch)], cwd=ROOT,
                           text=True, capture_output=True)
    if check.returncode != 0:
        raise ValueError("git apply --check failed: " + (check.stderr or check.stdout))
    subprocess.run(["git", "apply", str(patch)], cwd=ROOT, check=True)
    (ROOT / "build_logs" / "autonomous-engineering-job.json").write_text(
        json.dumps({"issue": ISSUE["number"], "title": ISSUE["title"],
                    "model": MODEL, "edited_paths": changed,
                    "edit_count": len(edits)}, indent=2), encoding="utf-8")
    return changed, result.get("summary", "")

ISSUE = find_issue()
if not ISSUE:
    print("ENGINEERING_JOB=NONE")
    sys.exit(0)

print(f"ENGINEERING_JOB=CLAIMED ISSUE={ISSUE['number']}")
detail = api("GET", f"/repos/{REPO}/issues/{ISSUE['number']}")
request = (detail.get("title","") + "\n\n" + detail.get("body","")).strip()
context, terms = select_context(request)

prompt = f"""You are the autonomous engineering agent for PrintVault 3MF.
A human submitted the following engineering request. Solve the request in the repository,
not merely by explaining it.

REQUEST:
{request}

RELEVANT SEARCH TERMS:
{json.dumps(terms)}

REPOSITORY CONTEXT:
{json.dumps(context, indent=2)}

Return JSON with:
{{"action":"patch"|"validate", "summary":"...", "edits":[{{"path":"...", "old":"...", "new":"..."}}]}}

Use action="validate" ONLY when the human request is explicitly asking to prove/verify an already implemented process. Use action="patch" for a request that requires code changes.

Rules:
- Make the smallest production-safe change that actually addresses the request.
- Each old block must be exact and unique.
- Do not modify VERSION, build_logs, secrets, or generated artifacts.
- Do not weaken/delete tests or lower gates just to obtain a pass.
- Add/update a regression test when the request is a bug fix and an appropriate test location exists.
- Do not claim success without a concrete code/test change.
- The orchestrator will build and validate the result afterward.
- If the request cannot be safely solved from the supplied context, return edits=[] and explain why in summary.
"""

last_error = ""
for attempt in range(1, 4):
    print(f"ENGINEERING_MODEL_ATTEMPT={attempt}/3")
    try:
        if last_error:
            prompt += "\nPrevious validator error; correct it:\n" + last_error
        result = request_model(prompt)
        changed, summary = apply_edits(result)
        print("ENGINEERING_PATCH=PASS")
        print("ENGINEERING_EDITED_PATHS=" + ",".join(changed))
        print("ENGINEERING_SUMMARY=" + summary)
        api("POST", f"/repos/{REPO}/issues/{ISSUE['number']}/comments",
            {"body": "Autonomous engineering job claimed. A validated source patch was generated; the build/test/repair gates are now running."})
        sys.exit(0)
    except Exception as exc:
        last_error = str(exc)
        print("ENGINEERING_PATCH=REJECTED " + last_error, file=sys.stderr)

raise SystemExit("Autonomous engineering agent could not produce a validated patch after 3 attempts.")
