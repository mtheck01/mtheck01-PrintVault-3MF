from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
engine = (ROOT / "src" / "PrintVault.Infrastructure" / "LibraryEngine.cs").read_text(encoding="utf-8")
program = (ROOT / "tools" / "PrintVault.Automation" / "Program.cs").read_text(encoding="utf-8")
workflow = (ROOT / ".github" / "workflows" / "autonomous-cycle.yml").read_text(encoding="utf-8")

# Every failed file must leave actionable evidence; a count-only failure is not enough
# to repair ingestion regressions without reproducing them.
required_engine = [
    "new ConcurrentQueue<object>()",
    "catch (Exception ex)",
    "ExceptionType = ex.GetType().FullName",
    "Message = ex.Message",
    "Detail = ex.ToString()",
    "ExistingRecord = existing.ContainsKey(path)",
    "FileStillExists = File.Exists(path)",
    "PRINTVAULT_SCAN_DIAGNOSTICS_PATH",
    "JsonSerializer.Serialize(scanFailures.ToArray()",
    "SCAN_DIAGNOSTIC_RECORDS=",
]
for item in required_engine:
    assert item in engine, f"Missing scan diagnostic invariant: {item}"

assert 'Path.Combine("build_logs", "scan-failures.json")' in program
assert 'Environment.SetEnvironmentVariable("PRINTVAULT_SCAN_DIAGNOSTICS_PATH", scanDiagnosticsPath)' in program
assert '"test_scan_failure_diagnostics.py"' in workflow, "Autonomous source gate must run the diagnostics contract"
print("PASS: per-file scan failures retain path, exception, and retry-relevant context")
