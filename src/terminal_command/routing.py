from __future__ import annotations

import os
import shlex
from typing import Protocol

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
    def __init__(self, model_router: ModelRouterLike | None = None):
        self.model_router = model_router

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

        shell = self._shell_route(raw)
        if shell:
            return shell

        deterministic = self._deterministic_route(raw)
        if deterministic:
            return deterministic

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
            explanation="No safe deterministic or model route was available.",
        )

    def _shell_route(self, raw: str) -> RouteResult | None:
        try:
            parts = shlex.split(raw, posix=os.name != "nt")
        except ValueError:
            return None
        if not parts:
            return None
        first = parts[0].lower()
        looks_like_path = first.startswith(("./", ".\\", "/"))
        if first not in _SHELL_COMMANDS and not first.endswith(_SHELL_SUFFIXES) and not looks_like_path:
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
        if normalized in {"show git status", "show me git status", "what changed in git"}:
            return self._nl("nl.git_status", "git_status", ["git", "status"])
        if normalized in {"show current directory", "where am i", "what directory am i in"}:
            command = ["cmd", "/c", "cd"] if os.name == "nt" else ["pwd"]
            return self._nl("nl.pwd", "current_directory", command)
        if normalized in {"list files", "show files", "show files here", "what files are here"}:
            command = ["cmd", "/c", "dir"] if os.name == "nt" else ["ls", "-la"]
            return self._nl("nl.list_files", "list_files", command)
        return None

    @staticmethod
    def _nl(rule_id: str, name: str, command: list[str]) -> RouteResult:
        return RouteResult(
            input_kind=InputKind.NATURAL_LANGUAGE,
            source="deterministic",
            action=Action(name=name, command=command, metadata={"rule_id": rule_id}),
            confidence=1.0,
            rule_id=rule_id,
        )
