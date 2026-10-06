from pathlib import Path
import ast

ROOT = Path(__file__).resolve().parents[1]
workflow = (ROOT / ".github" / "workflows" / "autonomous-cycle.yml").read_text(encoding="utf-8")
agent_path = ROOT / "tools" / "autonomous_engineering.py"
agent = agent_path.read_text(encoding="utf-8")

ast.parse(agent)

required_agent = [
    "ENGINEERING_JOB=CLAIMED",
    "ISSUE_PREFIX = \"[PrintVault-Autonomous]\"",
    "git apply --check",
    'encoding="utf-8"',
    'errors="replace"',
    "Model response must be a JSON object",
    "AUTHORITATIVE EXISTING FILES",
    "create=true",
    "def normalize_edit_path(path):",
    "def validate_edit_set(result):",
    "MODEL_ATTEMPTS",
    "validate_edit_set(result)",
    "absolute and outside repository",
    "Edit path escapes repository",
    "git diff --no-index",
    "OPENAI_API_KEY",
    "Do not modify VERSION",
    'action not in ("patch", "validate")',
    'action="validate" ONLY when',
    "HTTP 429",
    "Retry-After",
    "ENGINEERING_MODEL_RATE_LIMIT",
    "credit_balance_exhausted",
    "local_validate_existing_handoff",
    "ENGINEERING_FALLBACK=EXISTING_HANDOFF_CONTRACT_PASS",
    "def validate_existing_translation_alias_patch(issue):",
    "ENGINEERING_FALLBACK=EXISTING_TRANSLATION_ALIAS_PATCH",
    "ENGINEERING_API_CALLS=0",
    '"openai_requests": 0',
    "TranslationAliasNormalizationModule",
]
for marker in required_agent:
    assert marker in agent, f"autonomous engineering agent missing required guard/feature: {marker}"

required_workflow = [
    "issues: write",
    "tools\\autonomous_engineering.py",
    "ENGINEERING_INTAKE",
    "ENGINEERING_HANDOFF=PASS",
    "Deterministic autonomous intake preflight",
    "AUTONOMOUS_INTAKE_PREFLIGHT_PASS=$pass/2",
    "AUTONOMOUS_INTAKE_PREFLIGHT=PASS",
    "ENGINEERING_ISSUE_CLOSED=PASS",
]
for marker in required_workflow:
    assert marker in workflow, f"autonomous workflow missing intake integration: {marker}"

assert "VERSION" in agent and "build_logs" in agent
print("AUTONOMOUS_ENGINEERING_INTAKE_CONTRACT=PASS")


# Behavioral patch-boundary tests. Extract only the pure validators from the
# production agent so this test never calls GitHub or OpenAI.
module = ast.parse(agent)
wanted = [
    node for node in module.body
    if isinstance(node, ast.FunctionDef) and node.name in {"normalize_edit_path", "validate_edit_set"}
]
namespace = {
    "Path": Path,
    "re": __import__("re"),
    "ROOT": ROOT,
    "ALLOWED_ROOTS": ("src/", "tools/", "tests/", "automation/"),
    "BLOCKED_NAMES": {"VERSION", "build_logs"},
}
exec(compile(ast.Module(body=wanted, type_ignores=[]), str(agent_path), "exec"), namespace)
normalize_edit_path = namespace["normalize_edit_path"]
validate_edit_set = namespace["validate_edit_set"]

existing = "src/PrintVault.Infrastructure/LanguageIntelligenceService.cs"
existing_text = (ROOT / existing).read_text(encoding="utf-8", errors="replace")
unique_token = "public sealed class LanguageIntelligenceService"

assert validate_edit_set({
    "action": "patch",
    "edits": [{"path": existing, "old": unique_token, "new": unique_token}]
})

assert validate_edit_set({
    "action": "patch",
    "edits": [
        {"path": existing, "old": unique_token, "new": unique_token + " /*NEXT*/"},
        {"path": existing, "old": unique_token + " /*NEXT*/", "new": unique_token},
    ],
})

for bad in [
    {"action": "patch", "edits": [{"path": "C:/outside/repo.cs", "old": "x", "new": "y"}]},
    {"action": "patch", "edits": [{"path": "../outside.cs", "old": "x", "new": "y"}]},
    {"action": "patch", "edits": [{"path": existing, "create": True, "old": "", "new": "x"}]},
    {"action": "patch", "edits": [{"path": "src/does-not-exist.cs", "old": "x", "new": "y"}]},
    {"action": "patch", "edits": [{"path": existing, "old": "__PRINTVAULT_NO_SUCH_TEXT__", "new": "y"}]},
    {"action": "patch", "edits": [{"path": existing, "old": "using", "new": "y"}]},
    {"action": "patch", "edits": [
        {"path": existing, "old": "__PRINTVAULT_NO_SUCH_TEXT__", "new": "x"},
    ]},
]:
    try:
        validate_edit_set(bad)
    except ValueError:
        pass
    else:
        raise AssertionError(f"Unsafe patch was accepted: {bad}")

# Absolute paths inside the checkout normalize safely; outside paths do not.
inside = str((ROOT / existing).resolve())
assert normalize_edit_path(inside) == existing

print("AUTONOMOUS_PATCH_BOUNDARY_BEHAVIOR=PASS")
print("AUTONOMOUS_ENGINEERING_INTAKE_CONTRACT=PASS")
