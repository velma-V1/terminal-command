from __future__ import annotations

from pathlib import Path

from terminal_command.capabilities import CapabilityRegistry, default_capabilities
from terminal_command.packs.engineering import engineering_diagnose_workflow, register_engineering_pack
from terminal_command.policy import PolicyEngine


EXPECTED = {
    "git.diff",
    "git.log",
    "test.run",
    "build.run",
    "lint.run",
    "deps.inspect",
    "logs.tail",
    "process.inspect",
}


def _registry():
    registry = default_capabilities()
    register_engineering_pack(registry)
    return registry


def test_engineering_pack_registers_reusable_capabilities_without_replacing_core():
    registry = _registry()
    ids = {row["id"] for row in registry.describe()}
    assert EXPECTED <= ids
    assert registry.get("git.status") is not None


def test_python_project_command_detection(tmp_path):
    (tmp_path / "pyproject.toml").write_text("[project]\nname='demo'\nversion='0.1'\n", encoding="utf-8")
    registry = _registry()
    assert registry.invoke("test.run", {"root": str(tmp_path)}).command == ["python", "-m", "pytest", "-q"]
    assert registry.invoke("build.run", {"root": str(tmp_path)}).command == ["python", "-m", "build"]
    assert registry.invoke("lint.run", {"root": str(tmp_path)}).command == ["python", "-m", "compileall", "-q", "."]
    assert registry.invoke("deps.inspect", {"root": str(tmp_path)}).command == ["python", "-m", "pip", "list"]


def test_node_rust_and_go_project_command_detection(tmp_path):
    registry = _registry()

    node = tmp_path / "node"
    node.mkdir()
    (node / "package.json").write_text('{"scripts":{"test":"x","build":"x","lint":"x"}}', encoding="utf-8")
    assert registry.invoke("test.run", {"root": str(node)}).command == ["npm", "test"]
    assert registry.invoke("build.run", {"root": str(node)}).command == ["npm", "run", "build"]
    assert registry.invoke("lint.run", {"root": str(node)}).command == ["npm", "run", "lint"]

    rust = tmp_path / "rust"
    rust.mkdir()
    (rust / "Cargo.toml").write_text("[package]\nname='demo'\nversion='0.1.0'\n", encoding="utf-8")
    assert registry.invoke("test.run", {"root": str(rust)}).command == ["cargo", "test"]
    assert registry.invoke("build.run", {"root": str(rust)}).command == ["cargo", "build"]
    assert registry.invoke("lint.run", {"root": str(rust)}).command == ["cargo", "clippy"]

    go = tmp_path / "go"
    go.mkdir()
    (go / "go.mod").write_text("module demo\n", encoding="utf-8")
    assert registry.invoke("test.run", {"root": str(go)}).command == ["go", "test", "./..."]
    assert registry.invoke("build.run", {"root": str(go)}).command == ["go", "build", "./..."]


def test_engineering_actions_carry_working_directory_and_policy_is_conservative(tmp_path):
    (tmp_path / "pyproject.toml").write_text("[project]\nname='demo'\nversion='0.1'\n", encoding="utf-8")
    registry = _registry()
    test_action = registry.invoke("test.run", {"root": str(tmp_path)})
    assert test_action.cwd == str(tmp_path.resolve())
    assert PolicyEngine().evaluate(test_action).decision.value == "require_approval"

    diff_action = registry.invoke("git.diff", {"root": str(tmp_path)})
    assert diff_action.command == ["git", "diff"]
    assert diff_action.cwd == str(tmp_path.resolve())
    assert PolicyEngine().evaluate(diff_action).decision.value == "allow"


def test_log_and_process_inspection_are_read_only_capabilities(tmp_path):
    log = tmp_path / "app.log"
    log.write_text("hello", encoding="utf-8")
    registry = _registry()
    log_action = registry.invoke("logs.tail", {"path": str(log), "lines": 20})
    process_action = registry.invoke("process.inspect", {})
    assert log_action.metadata["read_only"] is True
    assert process_action.metadata["read_only"] is True
    assert PolicyEngine().evaluate(log_action).decision.value == "allow"
    assert PolicyEngine().evaluate(process_action).decision.value == "allow"


def test_engineering_diagnose_is_bounded_and_contains_no_mutation_capability():
    workflow = engineering_diagnose_workflow()
    ids = [step.capability_id for step in workflow.steps]
    assert ids == ["git.status", "deps.inspect", "test.run", "build.run"]
    assert len(ids) == 4
    assert all(not item.startswith(("fix.", "patch.", "git.commit")) for item in ids)
