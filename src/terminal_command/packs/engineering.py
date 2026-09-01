from __future__ import annotations

import json
import os
from pathlib import Path

from ..capabilities import ArgumentSpec, Capability, CapabilityRegistry
from ..contracts import Action
from ..workflows import Workflow, WorkflowStep


def _root(args: dict) -> Path:
    root = Path(args.get("root") or Path.cwd()).expanduser().resolve()
    if not root.exists() or not root.is_dir():
        raise ValueError(f"Project root does not exist: {root}")
    return root


def _project_kind(root: Path) -> str:
    if (root / "pyproject.toml").exists() or (root / "pytest.ini").exists() or (root / "setup.py").exists():
        return "python"
    if (root / "package.json").exists():
        return "node"
    if (root / "Cargo.toml").exists():
        return "rust"
    if (root / "go.mod").exists():
        return "go"
    return "unknown"


def _node_has_script(root: Path, script: str) -> bool:
    try:
        payload = json.loads((root / "package.json").read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return False
    return isinstance(payload.get("scripts"), dict) and script in payload["scripts"]


def _project_command(kind: str, operation: str, root: Path) -> list[str]:
    if kind == "python":
        return {
            "test": ["python", "-m", "pytest", "-q"],
            "build": ["python", "-m", "build"],
            "lint": ["python", "-m", "compileall", "-q", "."],
            "deps": ["python", "-m", "pip", "list"],
        }[operation]
    if kind == "node":
        if operation in {"test", "build", "lint"}:
            if not _node_has_script(root, operation):
                raise ValueError(f"package.json has no {operation} script")
            return ["npm", "test"] if operation == "test" else ["npm", "run", operation]
        return ["npm", "ls", "--depth=0"]
    if kind == "rust":
        return {
            "test": ["cargo", "test"],
            "build": ["cargo", "build"],
            "lint": ["cargo", "clippy"],
            "deps": ["cargo", "metadata", "--no-deps", "--format-version", "1"],
        }[operation]
    if kind == "go":
        return {
            "test": ["go", "test", "./..."],
            "build": ["go", "build", "./..."],
            "lint": ["gofmt", "-d", "."],
            "deps": ["go", "list", "-m", "all"],
        }[operation]
    raise ValueError(f"Could not detect supported project type at {root}")


def _project_action(capability_id: str, operation: str, args: dict, *, read_only: bool = False) -> Action:
    root = _root(args)
    kind = _project_kind(root)
    return Action(
        name=capability_id,
        command=_project_command(kind, operation, root),
        cwd=str(root),
        metadata={
            "capability_id": capability_id,
            "project_kind": kind,
            "read_only": read_only,
            "executes_project_code": operation in {"test", "build", "lint"},
        },
    )


def _log_tail_action(args: dict) -> Action:
    path = Path(args["path"]).expanduser().resolve()
    if not path.exists() or not path.is_file():
        raise ValueError(f"Log file does not exist: {path}")
    lines = int(args.get("lines", 100))
    if lines < 1 or lines > 10000:
        raise ValueError("lines must be between 1 and 10000")
    if os.name == "nt":
        escaped_path = str(path).replace("'", "''")
        command = ["powershell", "-NoProfile", "-Command", f"Get-Content -LiteralPath '{escaped_path}' -Tail {lines}"]
    else:
        command = ["tail", "-n", str(lines), str(path)]
    return Action("logs.tail", command, metadata={"capability_id": "logs.tail", "read_only": True})


def register_engineering_pack(registry: CapabilityRegistry) -> CapabilityRegistry:
    root_arg = (ArgumentSpec("root", kind="path", required=False, description="Project root"),)
    definitions = [
        Capability(
            id="git.diff",
            description="Show working-tree Git diff",
            arguments=root_arg,
            builder=lambda args: Action(
                "git.diff", ["git", "diff"], cwd=str(_root(args)), metadata={"capability_id": "git.diff", "read_only": True}
            ),
        ),
        Capability(
            id="git.log",
            description="Show recent Git commits",
            arguments=root_arg,
            builder=lambda args: Action(
                "git.log", ["git", "log", "-10", "--oneline"], cwd=str(_root(args)), metadata={"capability_id": "git.log", "read_only": True}
            ),
        ),
        Capability("test.run", "Run detected project tests", lambda args: _project_action("test.run", "test", args), root_arg),
        Capability("build.run", "Run detected project build", lambda args: _project_action("build.run", "build", args), root_arg),
        Capability("lint.run", "Run detected project lint/static check", lambda args: _project_action("lint.run", "lint", args), root_arg),
        Capability("deps.inspect", "Inspect detected project dependencies", lambda args: _project_action("deps.inspect", "deps", args, read_only=True), root_arg),
        Capability(
            id="logs.tail",
            description="Read the tail of a local log file",
            arguments=(ArgumentSpec("path", kind="path"), ArgumentSpec("lines", kind="int", required=False)),
            builder=_log_tail_action,
        ),
        Capability(
            id="process.inspect",
            description="Inspect running local processes",
            builder=lambda args: Action(
                "process.inspect",
                ["tasklist"] if os.name == "nt" else ["ps", "-ef"],
                metadata={"capability_id": "process.inspect", "read_only": True},
            ),
        ),
    ]
    for capability in definitions:
        if registry.get(capability.id) is None:
            registry.register(capability)
    return registry


def engineering_diagnose_workflow() -> Workflow:
    return Workflow(
        name="engineering.diagnose",
        description="Bounded evidence-first engineering diagnosis",
        steps=(
            WorkflowStep("git.status"),
            WorkflowStep("deps.inspect"),
            WorkflowStep("test.run"),
            WorkflowStep("build.run"),
        ),
    )
