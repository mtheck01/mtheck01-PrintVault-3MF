from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
program = (ROOT / "tools" / "PrintVault.Automation" / "Program.cs").read_text(encoding="utf-8")
contract = (ROOT / "tools" / "PrintVault.Automation" / "RepairReportContract.cs").read_text(encoding="utf-8")
service = (ROOT / "src" / "PrintVault.Infrastructure" / "WholeLibraryRootCauseAnalysisService.cs").read_text(encoding="utf-8")
workflow = (ROOT / ".github" / "workflows" / "autonomous-cycle.yml").read_text(encoding="utf-8")

# The producer emits camelCase envelope keys and RootCauseRow's CandidateCategory.
assert "catalog = models.Count" in service and "processed = recordsAnalyzed" in service
assert "rows = orderedRootCauseRows" in service
assert "CandidateCategory" in service

# The consumer must deserialize case-insensitively into the producer's actual row type.
assert "PropertyNameCaseInsensitive = true" in contract
assert "RootCauseRow[]? Rows" in contract
assert "row.CandidateCategory" in program
assert "InferredCategory" not in program
assert "RepairReportContract.Deserialize" in program
assert "RepairReportContract.Validate" in program

# All integrity checks happen before the database backup and any writeback.
validation = program.index("var reportError = RepairReportContract.Validate")
backup = program.index("repository.BackupDatabase()")
assert validation < backup
for marker in (
    'return "REPORT_CATALOG_MISMATCH"',
    'return "EXPECTED_CATALOG_MISMATCH"',
    'return "REPORT_ANALYSIS_COUNT_MISMATCH"',
    'return "REPORT_CONTAINS_ANALYSIS_FAILURES"',
    'return "REPORT_ROWS_MISSING"',
    'return "REPORT_ROW_UNKNOWN_ID"',
    'return "REPORT_ROW_PATH_MISMATCH"',
    'return "STALE_REPORT_RECORD"',
):
    assert marker in contract, f"Missing fail-closed repair-report validation: {marker}"

# The executable round-trip self-test must run in both the autonomous contract suite
# and the Windows build before this report can authorize writeback.
assert '"test_whole_library_repair_report_contract.py"' in workflow
assert 'selftest-repair-report-contract' in program
assert 'selftest-repair-report-contract' in workflow
assert 'selftest-repair-report-contract' in (ROOT / ".github" / "workflows" / "windows-build.yml").read_text(encoding="utf-8")

print("PASS: whole-library report producer/repair consumer schema and writeback gates align")
