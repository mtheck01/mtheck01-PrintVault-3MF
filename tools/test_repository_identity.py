from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path

EXPECTED = "mtheck01/mtheck01-PrintVault-3MF"
STALE = (
    "mtheck01/PrintVault-3MF",
    "github.com/mtheck01/PrintVault-3MF",
    "PrintVault-3MF.git",
    "git@github.com:mtheck01/PrintVault-3MF",
)

def run(*args: str) -> str:
    return subprocess.check_output(args, text=True, stderr=subprocess.STDOUT).strip()

errors: list[str] = []

repo_env = __import__("os").environ.get("GITHUB_REPOSITORY", "").strip()
if repo_env and repo_env != EXPECTED:
    errors.append(f"GITHUB_REPOSITORY is {repo_env!r}; expected {EXPECTED!r}")

try:
    origin = run("git", "remote", "get-url", "origin")
    normalized = re.sub(r"\.git$", "", origin).rstrip("/")
    expected_url = f"https://github.com/{EXPECTED}"
    if normalized.lower() != expected_url.lower():
        errors.append(f"origin is {origin!r}; expected {expected_url + '.git'!r}")
except subprocess.CalledProcessError as exc:
    errors.append(f"could not read git origin: {exc.output.strip()}")

try:
    tracked = run("git", "ls-files", "-z")
    paths = tracked.split("\0")
except subprocess.CalledProcessError as exc:
    errors.append(f"could not enumerate tracked files: {exc.output.strip()}")
    paths = []

root = Path(__file__).resolve().parents[1]
for rel in paths:
    if not rel:
        continue
    p = root / rel
    if not p.is_file():
        continue
    try:
        data = p.read_text(encoding="utf-8", errors="replace")
    except OSError:
        continue
    for stale in STALE:
        if stale in data:
            errors.append(f"stale repository reference {stale!r} in {rel}")

if errors:
    print("REPOSITORY IDENTITY AUDIT — FAIL")
    for error in errors:
        print(f"- {error}")
    raise SystemExit(1)

print("REPOSITORY IDENTITY AUDIT — PASS")
print(f"Canonical repository: {EXPECTED}")
print(f"Tracked files scanned: {sum(1 for p in paths if p)}")
print("No stale pre-rename repository references detected.")
