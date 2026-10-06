from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
VERSION = (ROOT / "VERSION").read_text(encoding="utf-8").strip()
services = sorted((ROOT / "src/PrintVault.Infrastructure").glob("*ChallengeService.cs"))
errors = []

if not services:
    errors.append("no challenge service files found")

stale_patterns = [
    re.compile(r'BUILD:\s*\d+\.\d+\.\d+'),
    re.compile(r'PrintVault 3MF\s+\d+\.\d+\.\d+'),
]

# User-facing application code must not carry stale challenge-version text either.
ui_files = list((ROOT / 'src/PrintVault').glob('*.xaml')) + list((ROOT / 'src/PrintVault').glob('*.cs'))
for path in ui_files:
    text = path.read_text(encoding='utf-8', errors='ignore')
    for pattern in stale_patterns:
        for match in pattern.finditer(text):
            errors.append(f"hard-coded user-facing version in {path.relative_to(ROOT)}: {match.group(0)}")

for path in services:
    text = path.read_text(encoding="utf-8", errors="ignore")
    for pattern in stale_patterns:
        for match in pattern.finditer(text):
            errors.append(f"hard-coded challenge version in {path.relative_to(ROOT)}: {match.group(0)}")
    if "AppVersion.Version" not in text:
        errors.append(f"challenge service does not consume AppVersion.Version: {path.relative_to(ROOT)}")

# VERSION is the sole release authority. No historical version sentinel belongs in this test.

if errors:
    print("CHALLENGE VERSION CONSISTENCY FAILED")
    for error in errors:
        print("-", error)
    raise SystemExit(1)

print(f"CHALLENGE VERSION CONSISTENCY PASSED — {len(services)} services — dynamic build/report version")
