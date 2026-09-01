from __future__ import annotations

import os
import shlex
import shutil
from typing import Protocol

from .capabilities import CapabilityRegistry, default_capabilities
from .contracts import Action, InputKind, RouteResult


class ModelRouterLike(Protocol):
    def route(self, text: str) -> RouteResult | None: ...


_SHELL_COMMANDS = {
    "apt", "bash", "cat", "cd", "cmd", "cargo", "copy", "del", "dir", "docker",
    "echo", "git", "go", "kill", "ls", "mkdir", "move", "node", "npm", "ollama",
    "pip", "pip3", "pnpm", "powershell", "pwsh", "pwd", "pytest", "python", "python3",
    "remove-item", "ren", "rm", "rmdir", "runas", "rustc", "shutdown", "sudo", "taskkill",
    "touch", "type", "where", "which", "winget", "wsl", "wsl.exe",
}
_SHELL_SUFFIXES = (".exe", ".bat", ".cmd", ".ps1", ".sh")
_SHELL_OPERATORS = ("|", ">", "<", "&&", "||", ";")


class Router:
    def __init__(
        self,
        model_router: ModelRouterLike | None = None,
        capabilities: CapabilityRegistry | None = None,
    ):
        self.model_router = model_router
        self.capabilities = capabilities or default_capabilities()

    def route(self, text: str) -> RouteResult:
        raw = text.strip()
        if not raw:
            return RouteResult(InputKind.UNRESOLVED, "unresolved", explanation="Empty input")

        if raw.startswith("/"):
            name = raw[1:].split(maxsplit=1)[0].lower()
            return RouteResult(
                input_kind=InputKind.SLASH,
                source="slash",
                action=Action(name=f"slash.{name}", command=[raw[1:]]),
                confidence=1.0,
                rule_id=f"slash.{name}",
            )

        deterministic = self._deterministic_route(raw)
        if deterministic:
            return deterministic

        capability_id = self.capabilities.resolve_id(raw)
        if capability_id is not None:
            try:
                action = self.capabilities.invoke(capability_id, {})
            except ValueError:
                action = None
            if action is not None:
                return RouteResult(
                    input_kind=InputKind.NATURAL_LANGUAGE,
                    source="capability_alias",
                    action=action,
                    confidence=1.0,
                    rule_id=f"capability:{capability_id}",
                )

        shell = self._shell_route(raw)
        if shell:
            return shell

        if self.model_router is not None:
            try:
                routed = self.model_router.route(raw)
            except Exception:
                routed = None
            if routed is not None:
                return routed

        return RouteResult(
            input_kind=InputKind.UNRESOLVED,
            source="unresolved",
            explanation="No safe deterministic, capability, shell, or model route was available.",
        )

    def _shell_route(self, raw: str) -> RouteResult | None:
        try:
            parts = shlex.split(raw, posix=os.name != "nt")
        except ValueError:
            return None
        if not parts:
            return None
        first = parts[0]
        lowered = first.lower()
        looks_like_path = lowered.startswith(("./", ".\\", "/"))
        installed = shutil.which(first) is not None
        if lowered not in _SHELL_COMMANDS and not lowered.endswith(_SHELL_SUFFIXES) and not looks_like_path and not installed:
            return None

        needs_shell = any(operator in raw for operator in _SHELL_OPERATORS)
        command = [raw] if needs_shell else parts
        return RouteResult(
            input_kind=InputKind.SHELL,
            source="shell",
            action=Action(
                name="shell",
                command=command,
                metadata={"raw_command": raw, "shell": needs_shell},
            ),
            confidence=1.0,
            rule_id="shell.explicit",
        )

    def _deterministic_route(self, raw: str) -> RouteResult | None:
        normalized = " ".join(raw.lower().split())
        builtins = {
            "show git status": ("git.status", "nl.git_status"),
            "show me git status": ("git.status", "nl.git_status"),
            "what changed in git": ("git.status", "nl.git_status"),
            "show current directory": ("system.cwd", "nl.pwd"),
            "where am i": ("system.cwd", "nl.pwd"),
            "what directory am i in": ("system.cwd", "nl.pwd"),
            "list files": ("files.list", "nl.list_files"),
            "show files": ("files.list", "nl.list_files"),
            "show files here": ("files.list", "nl.list_files"),
            "what files are here": ("files.list", "nl.list_files"),
        }
        match = builtins.get(normalized)
        if match is None:
            return None
        capability_id, rule_id = match
        try:
            action = self.capabilities.invoke(capability_id, {})
        except ValueError:
            return None
        return RouteResult(
            input_kind=InputKind.NATURAL_LANGUAGE,
            source="deterministic",
            action=action,
            confidence=1.0,
            rule_id=rule_id,
        )
