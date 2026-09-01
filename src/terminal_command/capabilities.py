from __future__ import annotations

import os
from dataclasses import dataclass, field
from typing import Any, Callable

from .contracts import Action


@dataclass(frozen=True, slots=True)
class ArgumentSpec:
    name: str
    kind: str = "str"
    required: bool = True
    choices: tuple[Any, ...] = ()
    description: str = ""


@dataclass(frozen=True, slots=True)
class CapabilityInvocation:
    capability_id: str
    arguments: dict[str, Any] = field(default_factory=dict)


Builder = Callable[[dict[str, Any]], Action]


@dataclass(frozen=True, slots=True)
class Capability:
    id: str
    description: str
    builder: Builder
    arguments: tuple[ArgumentSpec, ...] = ()
    aliases: tuple[str, ...] = ()


class CapabilityRegistry:
    def __init__(self) -> None:
        self._capabilities: dict[str, Capability] = {}
        self._aliases: dict[str, str] = {}
        self._context: dict[str, Any] = {}

    def set_context(self, **values: Any) -> None:
        self._context.update(values)

    def context(self) -> dict[str, Any]:
        return dict(self._context)

    def register(self, capability: Capability) -> None:
        if not capability.id or capability.id in self._capabilities:
            raise ValueError(f"Duplicate or empty capability id: {capability.id}")
        self._capabilities[capability.id] = capability
        for alias in capability.aliases:
            key = self._normalize(alias)
            if not key or key in self._aliases:
                raise ValueError(f"Duplicate or empty capability alias: {alias}")
            self._aliases[key] = capability.id

    def get(self, capability_id: str) -> Capability | None:
        return self._capabilities.get(capability_id)

    def resolve_id(self, text: str) -> str | None:
        if text in self._capabilities:
            return text
        return self._aliases.get(self._normalize(text))

    def invoke(self, capability_id: str, arguments: dict[str, Any] | None = None) -> Action:
        capability = self.get(capability_id)
        if capability is None:
            raise ValueError(f"Unknown capability: {capability_id}")
        values = dict(arguments or {})
        specs = {spec.name: spec for spec in capability.arguments}
        unknown = sorted(set(values) - set(specs))
        if unknown:
            raise ValueError(f"Unknown argument: {unknown[0]}")
        for spec in capability.arguments:
            if spec.required and spec.name not in values:
                raise ValueError(f"Missing required argument: {spec.name}")
            if spec.name not in values:
                continue
            value = values[spec.name]
            if not self._matches_kind(value, spec.kind):
                raise ValueError(f"Invalid type for {spec.name}: expected {spec.kind}")
            if spec.choices and value not in spec.choices:
                raise ValueError(f"Invalid value for {spec.name}: {value}")
        builder_values = dict(values)
        builder_values["__context__"] = dict(self._context)
        action = capability.builder(builder_values)
        action.metadata.setdefault("capability_id", capability.id)
        return action

    def describe(self) -> list[dict[str, Any]]:
        rows = []
        for capability_id in sorted(self._capabilities):
            capability = self._capabilities[capability_id]
            rows.append(
                {
                    "id": capability.id,
                    "description": capability.description,
                    "arguments": [
                        {
                            "name": spec.name,
                            "kind": spec.kind,
                            "required": spec.required,
                            "choices": list(spec.choices),
                            "description": spec.description,
                        }
                        for spec in capability.arguments
                    ],
                    "aliases": list(capability.aliases),
                }
            )
        return rows

    @staticmethod
    def _normalize(text: str) -> str:
        return " ".join(text.lower().split())

    @staticmethod
    def _matches_kind(value: Any, kind: str) -> bool:
        if kind in {"str", "path"}:
            return isinstance(value, str) and bool(value)
        if kind == "int":
            return isinstance(value, int) and not isinstance(value, bool)
        if kind == "float":
            return isinstance(value, (int, float)) and not isinstance(value, bool)
        if kind == "bool":
            return isinstance(value, bool)
        if kind == "list[str]":
            return isinstance(value, list) and all(isinstance(item, str) for item in value)
        return False


def default_capabilities() -> CapabilityRegistry:
    registry = CapabilityRegistry()
    registry.register(
        Capability(
            id="git.status",
            description="Show repository status",
            aliases=("show repository status", "show git status", "show me git status", "what changed in git"),
            builder=lambda args: Action(
                "git.status",
                ["git", "status"],
                cwd=args.get("__context__", {}).get("cwd"),
                metadata={"capability_id": "git.status", "read_only": True},
            ),
        )
    )
    registry.register(
        Capability(
            id="system.cwd",
            description="Show current directory",
            aliases=("show current directory", "where am i", "what directory am i in"),
            builder=lambda args: Action(
                "system.cwd",
                ["cmd", "/c", "cd"] if os.name == "nt" else ["pwd"],
                cwd=args.get("__context__", {}).get("cwd"),
                metadata={"capability_id": "system.cwd", "read_only": True},
            ),
        )
    )
    registry.register(
        Capability(
            id="files.list",
            description="List files in the current directory",
            aliases=("list files", "show files", "show files here", "what files are here"),
            builder=lambda args: Action(
                "files.list",
                ["cmd", "/c", "dir"] if os.name == "nt" else ["ls", "-la"],
                cwd=args.get("__context__", {}).get("cwd"),
                metadata={"capability_id": "files.list", "read_only": True},
            ),
        )
    )

    from .packs.daily import register_daily_pack
    from .packs.engineering import register_engineering_pack
    from .packs.security import register_security_pack
    from .remote import register_remote_capabilities
    from .web_adapter import register_web_capability

    register_engineering_pack(registry)
    register_daily_pack(registry)
    register_security_pack(registry)
    register_web_capability(registry)
    register_remote_capabilities(registry)
    return registry
