from __future__ import annotations

import json
import os
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Protocol

from .capabilities import CapabilityRegistry
from .contracts import ExecutionResult, PolicyDecision
from .policy import PolicyEngine, PolicyResult


@dataclass(frozen=True, slots=True)
class WorkflowStep:
    capability_id: str
    arguments: dict[str, Any] = field(default_factory=dict)
    required: bool = True


@dataclass(frozen=True, slots=True)
class Workflow:
    name: str
    steps: tuple[WorkflowStep, ...]
    description: str = ""


@dataclass(frozen=True, slots=True)
class WorkflowStepResult:
    capability_id: str
    status: str
    message: str = ""
    policy: PolicyResult | None = None
    execution: ExecutionResult | None = None


@dataclass(frozen=True, slots=True)
class WorkflowResult:
    name: str
    status: str
    steps: tuple[WorkflowStepResult, ...]


class ExecutorLike(Protocol):
    def execute(self, action) -> ExecutionResult: ...


class WorkflowStore:
    def __init__(self, path: str | Path):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._data = self._load()

    def save(self, workflow: Workflow) -> None:
        if not workflow.name.strip():
            raise ValueError("Workflow name cannot be empty")
        self._data["workflows"][workflow.name] = self._serialize(workflow)
        self._save()

    def get(self, name: str) -> Workflow | None:
        payload = self._data["workflows"].get(name)
        return self._deserialize(payload) if payload else None

    def list(self) -> list[Workflow]:
        return [self._deserialize(self._data["workflows"][name]) for name in sorted(self._data["workflows"])]

    def _load(self) -> dict[str, Any]:
        if not self.path.exists():
            return {"version": 1, "workflows": {}}
        try:
            payload = json.loads(self.path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            raise ValueError(f"Invalid workflow store: {self.path}") from exc
        if payload.get("version") != 1 or not isinstance(payload.get("workflows"), dict):
            raise ValueError("Unsupported workflow store schema")
        return payload

    def _save(self) -> None:
        temp = self.path.with_suffix(self.path.suffix + ".tmp")
        temp.write_text(json.dumps(self._data, indent=2, sort_keys=True), encoding="utf-8")
        os.replace(temp, self.path)

    @staticmethod
    def _serialize(workflow: Workflow) -> dict[str, Any]:
        return {
            "name": workflow.name,
            "description": workflow.description,
            "steps": [
                {"capability_id": step.capability_id, "arguments": step.arguments, "required": step.required}
                for step in workflow.steps
            ],
        }

    @staticmethod
    def _deserialize(payload: dict[str, Any]) -> Workflow:
        return Workflow(
            name=str(payload["name"]),
            description=str(payload.get("description", "")),
            steps=tuple(
                WorkflowStep(
                    capability_id=str(step["capability_id"]),
                    arguments=dict(step.get("arguments", {})),
                    required=bool(step.get("required", True)),
                )
                for step in payload.get("steps", [])
            ),
        )


class WorkflowRunner:
    def __init__(self, registry: CapabilityRegistry, policy: PolicyEngine, executor: ExecutorLike):
        self.registry = registry
        self.policy = policy
        self.executor = executor

    def run(self, workflow: Workflow, *, approved: bool = False) -> WorkflowResult:
        results: list[WorkflowStepResult] = []
        for step in workflow.steps:
            try:
                action = self.registry.invoke(step.capability_id, step.arguments)
            except ValueError as exc:
                results.append(WorkflowStepResult(step.capability_id, "invalid", str(exc)))
                return WorkflowResult(workflow.name, "invalid", tuple(results))

            policy = self.policy.evaluate(action)
            if policy.decision is PolicyDecision.DENY:
                results.append(WorkflowStepResult(step.capability_id, "denied", policy.reason, policy=policy))
                return WorkflowResult(workflow.name, "denied", tuple(results))
            if policy.decision is PolicyDecision.REQUIRE_APPROVAL and not approved:
                results.append(
                    WorkflowStepResult(step.capability_id, "approval_required", policy.reason, policy=policy)
                )
                return WorkflowResult(workflow.name, "approval_required", tuple(results))

            execution = self.executor.execute(action)
            status = execution.status
            message = execution.stdout or execution.stderr
            results.append(
                WorkflowStepResult(step.capability_id, status, message, policy=policy, execution=execution)
            )
            if step.required and status != "success":
                return WorkflowResult(workflow.name, "failed", tuple(results))
        return WorkflowResult(workflow.name, "success", tuple(results))
