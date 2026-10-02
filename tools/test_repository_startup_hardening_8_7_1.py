from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
repo=(ROOT/"src/PrintVault.Infrastructure/LibraryRepository.cs").read_text()
checks={
"safe integer reader":"ToInt32(SqliteDataReader r, int ordinal)" in repo and "long.TryParse" in repo,
"safe decimal reader":"ToDouble(SqliteDataReader r, int ordinal)" in repo and "double.TryParse" in repo,
"blank translation confidence normalized":"TranslationConfidence=0 WHERE TranslationConfidence IS NULL OR TRIM(CAST(TranslationConfidence AS TEXT))=\'\'" in repo,
"no direct translation confidence conversion":"Convert.ToInt32(r.GetValue(32))" not in repo,
"no direct object count conversion":"Convert.ToInt32(r.GetValue(12))" not in repo,
"version 8.7.8":"8.7.8" in (ROOT/"src/PrintVault.Core/Commands.cs").read_text(),
}
failed=[k for k,v in checks.items() if not v]
if failed: raise SystemExit("STARTUP HARDENING TEST FAILED: "+", ".join(failed))
print(f"STARTUP HARDENING 8.7.8 TEST PASSED — {len(checks)} checks")

repo = (ROOT / "src/PrintVault.Infrastructure/LibraryRepository.cs").read_text()
select_line = next(line for line in repo.splitlines() if "SelectColumns" in line and "TranslationEvidence" in line)
checks.update({
    "select includes 33rd translation evidence column": "OriginalLanguage,TranslatedTitle,TranslationConfidence,TranslationEvidence" in select_line,
    "read uses matching ordinals": "Text(r, 29)" in repo and "Text(r, 30)" in repo and "ToInt32(r, 31)" in repo and "Text(r, 32)" in repo,
    "no out-of-range ordinal 33": "Text(r, 33)" not in repo,
})

failed=[k for k,v in checks.items() if not v]
if failed: raise SystemExit("REPOSITORY STARTUP HARDENING 8.7.8 TEST FAILED: "+", ".join(failed))
print(f"REPOSITORY STARTUP HARDENING 8.7.8 TEST PASSED — {len(checks)} checks")
