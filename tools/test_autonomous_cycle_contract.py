from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
workflow = (ROOT / ".github" / "workflows" / "autonomous-cycle.yml").read_text(encoding="utf-8")

required = [
    "concurrency:",
    "workflow_dispatch:",
    "group: printvault-manual-engineering-cycle",
    "cancel-in-progress: false",
    "name: Autonomous Trigger Watchdog",
    "run_cycle=true",
    "run_cycle=false",
    "WATCHDOG_RESULT=UNVALIDATED",
    "WATCHDOG_RESULT=ALREADY_VALIDATED",
    "printvault/whole-library-validation",
    "needs: watchdog",
    "needs.watchdog.outputs.run_cycle == 'true'",
    "curl -fsSL",
    "git fetch origin main --prune",
    "$startingMainSha = (git rev-parse origin/main).Trim()",
    'Refusing to manufacture a release',
    "PROMOTION_GUARD_START_SHA=",
    "Promotion blocked: origin/main changed during validation.",
    "Invoke-WholeLibrary",
    "VALIDATION=PASS_NO_ACTIONABLE_CONFLICTS",
    "actions/upload-artifact@v4",
]

for item in required:
    assert item in workflow, f"Missing autonomous-chain invariant: {item}"

# Accept either PowerShell's quoted argument form or the historical unquoted form.
# The contract should validate the command semantics, not a fragile exact substring.
assert "rootcause" in workflow and "$reportDir" in workflow and "$expectedCatalog" in workflow, \
    "Missing autonomous whole-library rootcause invocation"
assert "repair $report $expectedCatalog" in workflow or \
       'repair $report $expectedCatalog' in workflow, \
    "Missing autonomous repair invocation"

# The workflow is intentionally manual-only while runner and whole-library
# validation are being stabilized. It must not cancel an active run.
assert "workflow_dispatch:" in workflow
assert "group: printvault-manual-engineering-cycle" in workflow
assert "cancel-in-progress: false" in workflow
assert "  push:" not in workflow
assert "  pull_request:" not in workflow
assert 'cron: "*/15 * * * *"' not in workflow

# Non-dispatch validation must remain zero-credit. OpenAI repair is opt-in only.
assert "AUTO_REPAIR: ${{ github.event_name == 'workflow_dispatch' && inputs.auto_repair || false }}" in workflow
assert "AUTO_REPAIR: ${{ inputs.auto_repair || true }}" not in workflow
assert "PRINTVAULT_ZERO_CREDIT: ${{ github.event_name != 'workflow_dispatch' || inputs.auto_repair == false }}" in workflow
assert "MAX_ATTEMPTS: ${{ github.event_name == 'workflow_dispatch' && inputs.max_attempts || '3' }}" in workflow
assert "default: true" in workflow[workflow.index("      deterministic_repair:"):workflow.index("      auto_repair:")]
assert "DETERMINISTIC_REPAIR: ${{ github.event_name == 'workflow_dispatch' && inputs.deterministic_repair || false }}" in workflow
assert "deterministic repair requires at least 2 whole-library validation passes" in workflow
assert "AI repair requires at least 3 whole-library validation passes" in workflow
assert "$needsRepair = $actionableConflicts -gt 0" in workflow
assert 'AUTONOMOUS_REPAIR=DISABLED' in workflow
assert 'AUTONOMOUS_REPAIR=ATTEMPT_LIMIT_REACHED' in workflow
assert 'AUTONOMOUS_REPAIR=DETERMINISTIC_WRITEBACK_APPLIED' in workflow
assert 'AUTONOMOUS_REPAIR=SOURCE_TEST_REPAIR_DISABLED' in workflow
assert 'AUTONOMOUS_REPAIR=EXHAUSTED' not in workflow
assert 'VALIDATION=PASS_NO_ACTIONABLE_CONFLICTS' in workflow
assert 'VALIDATION=PASS_NO_CONFLICTS' not in workflow

assert 'python ".\\tools\\forensic_repository_audit.py"' in workflow
assert "FORENSIC_AUDIT_PASS=$auditPass/2" in workflow
assert "FORENSIC_AUDIT_PRE_INTAKE=PASS" in workflow
assert "MODULE_PASS=$pass/2" in workflow
assert "Passes=2" in workflow
assert "MODULE_LOCKS=CREATED" in workflow
assert "MODULE_LOCK_GATE=PASS" in workflow
assert "Get-SourceFingerprint" in workflow
assert 'git status --porcelain=v1 -z -- . ' in workflow
assert "Every executable test named by the autonomous workflow must physically exist." in (ROOT / "tools" / "forensic_repository_audit.py").read_text(encoding="utf-8")
assert 'SKIP_DIRS = {".git", "bin", "obj"}' in (ROOT / "tools" / "forensic_repository_audit.py").read_text(encoding="utf-8")
assert "Whole-library reproducibility gate failed" in workflow
assert "WHOLE_LIBRARY_TWO_PASS=PASS" in workflow
assert "EXTERNAL_TIMEOUT_LOG_SAVED=" in workflow, "Timeouts must preserve redirected child stdout/stderr before throwing"
assert "EXTERNAL_TIMEOUT_STDOUT_TAIL_BEGIN" in workflow, "Timeout logs must expose final progress/error lines"
runner_start = workflow.index("function Invoke-ExternalWithHeartbeat")
runner_end = workflow.index('Write-CycleStatus "FORENSIC_AUDIT"', runner_start)
process_runner = workflow[runner_start:runner_end]
assert "$pollIntervalMilliseconds = 500" in process_runner, "External process completion must be polled promptly"
assert "$heartbeatIntervalMilliseconds = 15000" in process_runner, "Long-running processes must retain 15-second heartbeats"
assert "while (-not $proc.WaitForExit($pollIntervalMilliseconds))" in process_runner, \
    "The process runner must wait interruptibly for exit rather than sleep for a full heartbeat interval"
heartbeat_gate_start = process_runner.index("if ((($elapsed - $lastHeartbeat).TotalMilliseconds -ge $heartbeatIntervalMilliseconds)")
heartbeat_gate_end = process_runner.index("if ($processTimer.Elapsed.TotalSeconds -ge $timeoutSeconds)", heartbeat_gate_start)
heartbeat_block = process_runner[heartbeat_gate_start:heartbeat_gate_end]
assert 'Write-CycleStatus "EXTERNAL_RUNNING"' in heartbeat_block, "Long-running processes must publish live stage status"
assert "$lastHeartbeat = $processTimer.Elapsed" in heartbeat_block, "Heartbeat cadence must be measured between status updates"
assert "-or" in heartbeat_block and "$elapsed.TotalSeconds -ge $timeoutSeconds" in heartbeat_block, \
    "A process reaching its timeout must publish a final heartbeat before termination"
assert "$proc.WaitForExit(0)" in process_runner, "Timeout handling must recheck exit after heartbeat/status work"
assert "Start-Sleep -Seconds 15" not in process_runner, "A fixed heartbeat sleep must not delay completed child processes"
assert 'EXTERNAL_EXIT=$exitCode FILE=$filePath ELAPSED_MS=$elapsedMilliseconds' in process_runner, \
    "Each completed external process must report its elapsed time"
assert 'WHOLE_LIBRARY_ROOTCAUSE_TIMEOUT_SECONDS=2400' in workflow, "Root-cause analysis must meet the existing 40-minute budget"
assert '$runLog 2400' in workflow, "Root-cause timeout must match the existing diagnostic budget"
assert "timeout-minutes: 120" in workflow, "Keep the existing bounded job budget while validating the optimization"
assert "Invoke-WholeLibrary -Pass 1" in workflow
assert "Invoke-WholeLibrary -Pass 2" in workflow
assert "if ($script:SourceModuleLocks.Count -ne $script:SourceModuleCount)" in workflow, \
    "Module-lock gate must derive its expected count from the authoritative source-test list"
assert "expected $($script:SourceModuleCount), found $($script:SourceModuleLocks.Count)" in workflow, \
    "Module-lock failure must report the dynamic expected and actual counts"
assert "if ($script:SourceModuleLocks.Count -ne 41)" not in workflow, \
    "Hard-coded module-lock count can drift when tests are added"
assert "timeout-minutes: 120" in workflow
assert workflow.index("$startingMainSha = (git rev-parse origin/main).Trim()") < workflow.index("Promotion blocked: origin/main changed during validation.")

assert 'Install-Candidate' in workflow
assert 'Start-FinalApp' in workflow
assert 'Close-PrintVault $finalApp' in workflow
assert 'FINAL_APP_SMOKE_CLOSED=PASS' in workflow
assert 'Cleanup PrintVault process' in workflow
assert 'PRINTVAULT_PROCESS_CLEANUP=PASS' in workflow
assert 'APPLICATION=DEFERRED_UNTIL_VALIDATION_PASS' in workflow
assert workflow.index('LEGACY_TAXONOMY_CLEANUP') < workflow.index('FINAL_APP_SMOKE')
final_app_start = workflow.index('function Start-FinalApp')
final_app_end = workflow.index('function Close-PrintVault', final_app_start)
final_app = workflow[final_app_start:final_app_end]
assert '$proc.Refresh()' in final_app, 'The smoke check must refresh process state before reading its window handle'
assert '$proc.MainWindowHandle' in final_app and '$proc.Responding' in final_app, \
    'UI readiness requires a visible main window and a responsive app process'
assert 'if ($mainWindowHandle -ne [IntPtr]::Zero -and $responding)' in final_app, \
    'OPEN_READY=PASS must be conditional on a responsive main window'
assert 'APPLICATION=OPEN_READY=NOT_VERIFIED' in final_app and 'ui-smoke-not-verified.txt' in final_app, \
    'Headless service sessions must retain an explicit not-verified UI result'
assert final_app.index('if ($mainWindowHandle -ne [IntPtr]::Zero -and $responding)') < final_app.index('APPLICATION=OPEN_READY=PASS')
assert 'Write-Host "APPLICATION=OPEN_READY=PASS"' not in workflow.replace(final_app, ''), \
    'No unconditional UI readiness success may appear outside the guarded smoke path'
assert 'LEGACY_REGISTRY_ENTRIES_REMAINING=0' in workflow
assert 'LEGACY_REGISTRY_ENTRIES_REMAINING=' in (ROOT / "tools" / "PrintVault.Automation" / "Program.cs").read_text(encoding="utf-8")
assert 'CleanupLegacyTaxonomy' in (ROOT / "src" / "PrintVault.Infrastructure" / "OrganizationService.cs").read_text(encoding="utf-8")
assert 'if (IsCustomCategory(top) && !IsLegacyCategory(top)) return top;' in (ROOT / "src" / "PrintVault.Infrastructure" / "LibraryEngine.cs").read_text(encoding="utf-8")
assert '!IsLegacyCategory(x)' in (ROOT / "src" / "PrintVault" / "MainWindow.xaml.cs").read_text(encoding="utf-8")

assert 'INITIAL_VERSION_BUMP=PASS' in workflow
assert workflow.index('INITIAL_VERSION_BUMP=PASS') < workflow.index('$candidate = Invoke-CandidateBuild')
assert 'PRINTVAULT_AUTONOMOUS_CLASSIFICATION_ONLY = "1"' in workflow
assert 'autonomousClassificationOnly' in (ROOT / "src" / "PrintVault.Infrastructure" / "LibraryEngine.cs").read_text(encoding="utf-8")
assert 'doHash = !autonomousClassificationOnly' in (ROOT / "src" / "PrintVault.Infrastructure" / "LibraryEngine.cs").read_text(encoding="utf-8")
assert 'doThumbnail = !autonomousClassificationOnly' in (ROOT / "src" / "PrintVault.Infrastructure" / "LibraryEngine.cs").read_text(encoding="utf-8")
print("PASS: autonomous cycle contract and promotion safety gate")

# The validation runner must use only the isolated test library and must never
# fall back to the production D: library or an arbitrary environment path.
assert r'D:\3d print files test' in workflow, "Missing isolated test-library root"
assert r'D:\3d print files' in workflow, "Missing production-library safety guard"
assert 'Refusing to scan unapproved test-library path' in workflow
assert 'PRODUCTION_LIBRARY_SCAN=DISABLED' in workflow
assert 'DRIVE_ROOT_SCAN=DISABLED' in workflow


# Spaced test-library paths must not cross the autonomous process boundary as
# positional command-line arguments. The scanner receives the exact path via
# PRINTVAULT_TEST_LIBRARY_ROOT instead.
assert '$env:PRINTVAULT_TEST_LIBRARY_ROOT = $testLibraryRoot' in workflow,     "Autonomous cycle must export the isolated test root"
assert 'Environment.GetEnvironmentVariable("PRINTVAULT_TEST_LIBRARY_ROOT")' in     (ROOT / "tools" / "PrintVault.Automation" / "Program.cs").read_text(encoding="utf-8"),     "Automation scanner must consume PRINTVAULT_TEST_LIBRARY_ROOT"
assert 'scan", "", "Deep"' in workflow,     "Autonomous scan must not pass the spaced test root as a positional argument"
assert 'cleanup", "")' in workflow,     "Autonomous cleanup must not pass the spaced test root as a positional argument"
assert 'LEGACY_CATEGORIES_REMAINING=0' in workflow
assert 'LEGACY_TAGS_REMAINING=0' in workflow
assert 'LEGACY_FOLDERS_REMAINING=0' in workflow
assert 'FILES_BEFORE=' in workflow and 'FILES_AFTER=' in workflow
assert 'Legacy taxonomy cleanup failed' in workflow
assert 'Test-library organization failed' not in workflow

current_modular_tests = [
    "test_8_6_library_lifecycle.py", "test_browse_sorting.py",
    "test_category_dropdown_fix3.py", "test_category_navigation_fix2.py",
    "test_category_reconciliation.py", "test_change_category_readability.py",
    "test_clear_scan_race.py", "test_consolidation_deep.py",
    "test_full_library_intelligence_9_0_11.py", "test_intelligence_8_4.py",
    "test_language_intelligence_module.py", "test_library_intelligence_health.py",
    "test_scan_stability.py", "test_schema_migration.py",
    "test_semantic_evidence_fusion_9_0_13.py", "test_semantic_evidence_taxonomy_contract_9_0_24.py",
    "test_semantic_intelligence.py", "test_semantic_relationship_integration_9_0_20.py",
    "test_smart_category_engine.py", "test_smart_category_review.py",
    "test_whole_library_entity_coverage_v1.py",
    "test_autonomous_engineering_intake.py"
]
for test_name in current_modular_tests:
    assert test_name in workflow, f"Current modular regression test is not wired into autonomous cycle: {test_name}"

assert 'Refusing to manufacture a release' in workflow
assert 'WHOLE_LIBRARY_VALIDATION_STATUS=' in workflow
assert '--force-with-lease=refs/heads/${branch}:${expectedBranchSha}' in workflow
assert 'Never use an unleased force push.' in workflow
assert "if ($arg -match '\\s' -or $arg -match '[&|;<>]')" in workflow, "External-process wrapper must quote whitespace/shell-sensitive arguments"
assert 'if ($env:PRINTVAULT_ZERO_CREDIT -eq "1") {' in workflow, "AI repair boundary must hard-block zero-credit execution"

# Candidate build prerequisites must fail closed. A native-process failure cannot
# be allowed to have its exit code overwritten by the next audit command.
assert 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\\tools\\release_preflight.ps1" -Root "$PWD"' in workflow and 'Release preflight failed after candidate source changes' in workflow, "Candidate release preflight exit code is not enforced"
assert 'python ".\\tools\\static_audit.py"' in workflow and 'Static audit failed after candidate source changes' in workflow, "Candidate static-audit exit code is not enforced"
assert 'python ".\\tools\\forensic_repository_audit.py"' in workflow and 'Forensic repository audit failed after candidate source changes' in workflow, "Candidate forensic-audit exit code is not enforced"
