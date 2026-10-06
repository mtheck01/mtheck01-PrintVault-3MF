from __future__ import annotations

import os
import re
import subprocess
from pathlib import Path

EXPECTED = "mtheck01/mtheck01-PrintVault-3MF"

# Match only the OLD repository identifiers. Do not use "PrintVault-3MF.git"
# by itself because that suffix also exists in the current repository name.
STALE = (
    "mtheck01/PrintVault-3MF",
    "https://github.com/mtheck01/PrintVault-3MF",
    "https://github.com/mtheck01/PrintVault-3MF.git",
    "git@github.com:mtheck01/PrintVault-3MF.git",
)


def run(*args: str) -> str:
    return subprocess.check_output(args, text=True, stderr=subprocess.STDOUT).strip()


errors: list[str] = []

repo_env = os.environ.get("GITHUB_REPOSITORY", "").strip()
if repo_env and repo_env != EXPECTED:
    errors.append(f"GITHUB_REPOSITORY is {repo_env!r}; expected {EXPECTED!r}")

try:
    origin = run("git", "remote", "get-url", "origin")
    normalized = re.sub(r"\\.git$", "", origin).rstrip("/")
    expected_url = f"https://github.com/{EXPECTED}"
    if normalized.lower() != expected_url.lower():
        errors.append(f"origin is {origin!r}; expected {expected_url + '.git'!r}")
except subprocess.CalledProcessError as exc:
    errors.append(f"could not read git origin: {exc.output.strip()}")

try:
    tracked = run("git", "ls-files", "-z")
    paths = [p for p in tracked.split("\0") if p]
except subprocess.CalledProcessError as exc:
    errors.append(f"could not enumerate tracked files: {exc.output.strip()}")
    paths = []

root = Path(__file__).resolve().parents[1]
audit_file = Path(__file__).resolve()

scanned = 0
for rel in paths:
    p = root / rel
    if not p.is_file() or p.resolve() == audit_file:
        continue

    scanned += 1
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
print(f"Tracked files scanned: {scanned}")
print("No stale pre-rename repository references detected.")
