from __future__ import annotations

from dataclasses import asdict, dataclass, field
from enum import Enum
from typing import Any


class InputKind(str, Enum):
    SHELL = "shell"
    SLASH = "slash"
    NATURAL_LANGUAGE = "natural_language"
    UNRESOLVED = "unresolved"


class RiskLevel(str, Enum):
    READ_ONLY = "read_only"
    LOW = "low"
    MUTATING = "mutating"
    PRIVILEGED = "privileged"
    DESTRUCTIVE = "destructive"
    CATASTROPHIC = "catastrophic"
    UNKNOWN = "unknown"


class PolicyDecision(str, Enum):
    ALLOW = "allow"
    REQUIRE_APPROVAL = "require_approval"
    DENY = "deny"


@dataclass(slots=True)
class Action:
    name: str
    command: list[str] = field(default_factory=list)
    backend: str = "native"
    cwd: str | None = None
    metadata: dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)

    @classmethod
    def from_dict(cls, payload: dict[str, Any]) -> "Action":
        return cls(
            name=str(payload["name"]),
            command=[str(part) for part in payload.get("command", [])],
            backend=str(payload.get("backend", "native")),
            cwd=payload.get("cwd"),
            metadata=dict(payload.get("metadata", {})),
        )


@dataclass(slots=True)
class RouteResult:
    input_kind: InputKind
    source: str
    action: Action | None = None
    confidence: float | None = None
    rule_id: str | None = None
    model_id: str | None = None
    explanation: str | None = None

    def to_dict(self) -> dict[str, Any]:
        return {
            "input_kind": self.input_kind.value,
            "source": self.source,
            "action": self.action.to_dict() if self.action else None,
            "confidence": self.confidence,
            "rule_id": self.rule_id,
            "model_id": self.model_id,
            "explanation": self.explanation,
        }

    @classmethod
    def from_dict(cls, payload: dict[str, Any]) -> "RouteResult":
        action = payload.get("action")
        return cls(
            input_kind=InputKind(payload["input_kind"]),
            source=str(payload["source"]),
            action=Action.from_dict(action) if action else None,
            confidence=payload.get("confidence"),
            rule_id=payload.get("rule_id"),
            model_id=payload.get("model_id"),
            explanation=payload.get("explanation"),
        )


@dataclass(slots=True)
class ExecutionResult:
    backend: str
    exit_code: int | None
    stdout: str
    stderr: str
    duration_ms: float
    status: str

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)
