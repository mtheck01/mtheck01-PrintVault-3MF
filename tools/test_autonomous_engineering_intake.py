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
    "git grep",
    "git apply --check",
    "OPENAI_API_KEY",
    "Do not modify VERSION",
]
for marker in required_agent:
    assert marker in agent, f"autonomous engineering agent missing required guard/feature: {marker}"

required_workflow = [
    "issues: write",
    "tools\\autonomous_engineering.py",
    "ENGINEERING_INTAKE",
    "ENGINEERING_HANDOFF=PASS",
    "ENGINEERING_ISSUE_CLOSED=PASS",
]
for marker in required_workflow:
    assert marker in workflow, f"autonomous workflow missing intake integration: {marker}"

assert "VERSION" in agent and "build_logs" in agent
print("AUTONOMOUS_ENGINEERING_INTAKE_CONTRACT=PASS")
