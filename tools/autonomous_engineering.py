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

def run_text(args, *, check=False, capture_output=True):
    return subprocess.run(
        args, cwd=ROOT, text=True, encoding="utf-8", errors="replace",
        capture_output=capture_output, check=check,
    )

def git_files():
    p = run_text(["git", "ls-files"], check=True)
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
            p = run_text(["git", "grep", "-Il", "-i", "-E", pattern, "--", root])
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

class QuotaExhausted(RuntimeError):
    """Permanent billing/quota failure; never retry automatically."""
    pass

def mark_issue_api_quota_blocked(issue):
    marker = "[AUTONOMOUS_ENGINEERING_BLOCKED:API_QUOTA]"
    detail = api("GET", f"/repos/{REPO}/issues/{issue['number']}")
    body = detail.get("body") or ""
    if marker not in body:
        new_body = body.rstrip() + "\n\n" + marker + "\n"
        api("PATCH", f"/repos/{REPO}/issues/{issue['number']}", {"body": new_body})
    api("POST", f"/repos/{REPO}/issues/{issue['number']}/comments",
        {"body": "AUTONOMOUS_ENGINEERING_BLOCKED=API_QUOTA. No further OpenAI repair requests will be attempted until this block is explicitly cleared after API credits/quota are restored."})

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

    # Distinguish transient rate limiting from permanent quota exhaustion.
    # A quota failure must never be retried because it cannot succeed until
    # billing/quota is restored and repeated calls only burn time/resources.
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
            if "insufficient_quota" in body or "credit_balance_exhausted" in body:
                raise QuotaExhausted(f"OpenAI API HTTP 429: quota exhausted: {body[:4000]}")
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

def normalize_edit_path(path):
    """Convert model paths to canonical repository-relative POSIX paths.

    Accept an absolute runner path only when it resolves inside this checkout;
    reject absolute paths outside the repository and all traversal escapes.
    """
    if not isinstance(path, str) or not path.strip():
        raise ValueError(f"Edit path must be a non-empty string: {path!r}")
    raw = path.strip().replace("\\\\", "/")
    root = ROOT.resolve().as_posix().rstrip("/")
    raw_cmp = raw.lower() if re.match(r"^[A-Za-z]:/", raw) else raw
    root_cmp = root.lower() if re.match(r"^[A-Za-z]:/", root) else root
    if raw_cmp == root_cmp or raw_cmp.startswith(root_cmp + "/"):
        normalized = raw[len(root):].lstrip("/")
    elif re.match(r"^[A-Za-z]:/", raw) or raw.startswith("/"):
        raise ValueError(f"Edit path is absolute and outside repository: {path!r}")
    else:
        normalized = raw

    candidate = (ROOT / normalized).resolve()
    try:
        relative = candidate.relative_to(ROOT.resolve())
    except ValueError as exc:
        raise ValueError(f"Edit path escapes repository: {path!r}") from exc
    result = relative.as_posix()
    if not result.startswith(ALLOWED_ROOTS):
        raise ValueError(f"Edit path is outside allowed roots: {path!r}")
    return result

def apply_edits(result):
    if not isinstance(result, dict):
        raise ValueError(f"Model response must be a JSON object; got {type(result).__name__}.")
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
        if not isinstance(summary, str) or not summary.strip() or any(
            x in summary.lower() for x in ("cannot", "unable", "insufficient", "not enough context")
        ):
            raise ValueError("Validate-only result must contain a concrete successful rationale.")
        (ROOT / "build_logs").mkdir(exist_ok=True)
        (ROOT / "build_logs" / "autonomous-engineering-job.json").write_text(
            json.dumps({"issue": ISSUE["number"], "title": ISSUE["title"], "model": MODEL,
                        "action": "validate", "edited_paths": [], "edit_count": 0,
                        "summary": summary}, indent=2), encoding="utf-8")
        return [], summary

    originals = {}
    changed = []
    for i, edit in enumerate(edits, 1):
        if not isinstance(edit, dict):
            raise ValueError(f"Edit {i} must be an object.")
        path, old, new = edit.get("path"), edit.get("old"), edit.get("new")
        path = normalize_edit_path(path)
        if Path(path).name in BLOCKED_NAMES or "build_logs" in Path(path).parts:
            raise ValueError(f"Edit {i} targets a blocked path: {path}")

        target = ROOT / path
        create = edit.get("create", False) is True
        if create:
            if not isinstance(old, str) or old != "":
                raise ValueError(f"Create edit {i} must use old=''.")
            if not isinstance(new, str) or not new.strip():
                raise ValueError(f"Create edit {i} must contain non-empty UTF-8 file contents.")
            if target.exists():
                raise ValueError(f"Create edit {i} targets an existing file: {path}")
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(new, encoding="utf-8", newline="")
            originals[path] = None
            changed.append(path)
            continue

        if not isinstance(old, str) or not isinstance(new, str):
            raise ValueError(f"Edit {i} must contain string old/new values.")
        if not target.exists():
            raise ValueError(f"Missing edit target: {path}")
        current = target.read_text(encoding="utf-8", errors="replace")
        occurrences = current.count(old)
        if occurrences != 1:
            raise ValueError(f"Edit {i} requires exactly one match in {path}; found {occurrences}.")
        if path not in originals:
            originals[path] = current
        target.write_text(current.replace(old, new, 1), encoding="utf-8")
        if path not in changed:
            changed.append(path)

    existing = [path for path in changed if originals[path] is not None]
    created = [path for path in changed if originals[path] is None]
    diff_parts = []
    if existing:
        tracked = run_text(["git", "diff", "--", *existing])
        if tracked.returncode != 0:
            raise ValueError("git diff failed while generating the autonomous engineering patch.")
        diff_parts.append(tracked.stdout)
    for path in created:
        new_file = run_text(["git", "diff", "--no-index", "--binary", "--", os.devnull, str(ROOT / path)])
        if new_file.returncode not in (0, 1):
            raise ValueError(f"git diff --no-index failed for new file {path}: {new_file.stderr}")
        diff_parts.append(new_file.stdout)

    patch_text = "".join(x for x in diff_parts if x)
    if not patch_text.startswith("diff --git "):
        raise ValueError("Generated patch is empty or malformed.")

    patch = ROOT / "build_logs" / "autonomous-engineering.patch"
    patch.parent.mkdir(exist_ok=True)
    patch.write_text(patch_text, encoding="utf-8")

    for path, original in originals.items():
        target = ROOT / path
        if original is None:
            if target.exists():
                target.unlink()
        else:
            target.write_text(original, encoding="utf-8")

    check = run_text(["git", "apply", "--check", str(patch)])
    if check.returncode != 0:
        raise ValueError("git apply --check failed: " + (check.stderr or check.stdout))
    run_text(["git", "apply", str(patch)], check=True)

    summary = result.get("summary", "")
    if not isinstance(summary, str):
        summary = str(summary)
    (ROOT / "build_logs" / "autonomous-engineering-job.json").write_text(
        json.dumps({"issue": ISSUE["number"], "title": ISSUE["title"], "model": MODEL,
                    "edited_paths": changed, "edit_count": len(edits),
                    "summary": summary}, indent=2), encoding="utf-8")
    return changed, summary

ISSUE = find_issue()
if not ISSUE:
    print("ENGINEERING_JOB=NONE")
    sys.exit(0)

detail = api("GET", f"/repos/{REPO}/issues/{ISSUE['number']}")
blocked_marker = "[AUTONOMOUS_ENGINEERING_BLOCKED:API_QUOTA]"
if blocked_marker in (detail.get("body") or ""):
    print(f"ENGINEERING_JOB=BLOCKED_API_QUOTA ISSUE={ISSUE['number']}")
    print("ENGINEERING_API_CALLS=0")
    sys.exit(0)

print(f"ENGINEERING_JOB=CLAIMED ISSUE={ISSUE['number']}")
request = (detail.get("title","") + "\n\n" + detail.get("body","")).strip()
def local_validate_existing_handoff(issue):
    """Validate the already-implemented autonomous handoff when model credits are unavailable.

    This fallback is deliberately narrow: it applies only to the handoff acceptance issue,
    proves the intake contract locally, and never fabricates a source patch. Downstream
    build, module, whole-library, smoke, promotion, and release gates remain mandatory.
    """
    if issue.get("title") != "[PrintVault-Autonomous] Finish end-to-end autonomous engineering handoff":
        return None
    if not (ROOT / "tools" / "test_autonomous_engineering_intake.py").exists():
        return None
    check = run_text([sys.executable, str(ROOT / "tools" / "test_autonomous_engineering_intake.py")])
    if check.returncode != 0:
        raise RuntimeError("Existing autonomous handoff contract failed: " + (check.stdout + check.stderr)[-4000:])
    summary = "Model credits unavailable; existing autonomous engineering handoff contract validated locally. No source patch was fabricated; downstream build/test/whole-library/promotion gates remain required."
    (ROOT / "build_logs").mkdir(exist_ok=True)
    (ROOT / "build_logs" / "autonomous-engineering-job.json").write_text(
        json.dumps({"issue": issue["number"], "title": issue["title"],
                    "model": MODEL, "action": "validate", "edited_paths": [],
                    "edit_count": 0, "summary": summary,
                    "fallback": "existing_handoff_contract"} , indent=2), encoding="utf-8")
    return summary

context, terms = select_context(request)
available_files = git_files()
focus_paths = [
    "src/PrintVault.Infrastructure/LanguageIntelligenceService.cs",
    "src/PrintVault.Infrastructure/Intelligence/LanguageNormalizationStage.cs",
    "src/PrintVault.Infrastructure/MultilingualEntityService.cs",
    "src/PrintVault.Infrastructure/MultilingualMetadataService.cs",
    "tools/test_language_intelligence_module.py",
    "tools/test_language_detection_challenge.py",
    "tools/Verify-ModularArchitecture.ps1",
]
focus_paths = [p for p in focus_paths if p in available_files]

prompt = f"""You are the autonomous engineering agent for PrintVault 3MF.
A human submitted the following engineering request. Solve the request in the repository,
not merely by explaining it.

REQUEST:
{request}

RELEVANT SEARCH TERMS:
{json.dumps(terms)}

AUTHORITATIVE EXISTING FILES (existing target paths MUST be copied exactly from this list):
{json.dumps(available_files, indent=2)}

ISSUE-FOCUSED FILES TO INSPECT FIRST:
{json.dumps(focus_paths, indent=2)}

REPOSITORY CONTEXT:
{json.dumps(context, indent=2)}

Return JSON with:
{{"action":"patch"|"validate", "summary":"...", "edits":[{{"path":"...", "old":"...", "new":"..."}}]}}

Use action="validate" ONLY when the human request is explicitly asking to prove/verify an already implemented process. Use action="patch" for a request that requires code changes.

Rules:
- Make the smallest production-safe change that actually addresses the request.
- Each old block must be exact and unique.
- NEVER invent an existing target path. Existing edit paths must be copied verbatim from AUTHORITATIVE EXISTING FILES.
- If the modular boundary requires a genuinely new file, use {{"path":"src/...","create":true,"old":"","new":"<complete UTF-8 contents>"}}; create=true is the only allowed way to create a new file.
- A create=true path must be under an allowed root and must not already exist.
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
    except QuotaExhausted as exc:
        last_error = str(exc)
        print("ENGINEERING_PATCH=BLOCKED_API_QUOTA " + last_error, file=sys.stderr)
        mark_issue_api_quota_blocked(ISSUE)
        print("ENGINEERING_API_CALLS=1")
        sys.exit(0)
    except Exception as exc:
        last_error = str(exc)
        print("ENGINEERING_PATCH=REJECTED " + last_error, file=sys.stderr)
        if "credit_balance_exhausted" in last_error or "insufficient_quota" in last_error:
            fallback_summary = local_validate_existing_handoff(ISSUE)
            if fallback_summary:
                print("ENGINEERING_FALLBACK=EXISTING_HANDOFF_CONTRACT_PASS")
                print("ENGINEERING_PATCH=PASS")
                print("ENGINEERING_EDITED_PATHS=")
                print("ENGINEERING_SUMMARY=" + fallback_summary)
                api("POST", f"/repos/{REPO}/issues/{ISSUE['number']}/comments",
                    {"body": "Autonomous engineering intake validated the existing handoff contract locally because the repair-model account has no API credits. No source patch was fabricated; downstream build/test/whole-library/promotion/release gates remain mandatory."})
                sys.exit(0)

raise SystemExit("Autonomous engineering agent could not produce a validated patch after 3 attempts.")
