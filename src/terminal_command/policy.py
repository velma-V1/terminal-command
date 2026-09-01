from __future__ import annotations

from dataclasses import dataclass

from .contracts import Action, PolicyDecision, RiskLevel


@dataclass(frozen=True, slots=True)
class PolicyResult:
    decision: PolicyDecision
    risk: RiskLevel
    reason: str


class PolicyEngine:
    def evaluate(self, action: Action) -> PolicyResult:
        text = self._normalized(action)

        if self._catastrophic(text):
            return PolicyResult(PolicyDecision.DENY, RiskLevel.CATASTROPHIC, "Catastrophic system-wide destructive pattern")
        if action.metadata.get("security") is True:
            return PolicyResult(PolicyDecision.REQUIRE_APPROVAL, RiskLevel.PRIVILEGED, "Security action requires explicit approval")
        if action.metadata.get("remote") is True:
            return PolicyResult(PolicyDecision.REQUIRE_APPROVAL, RiskLevel.PRIVILEGED, "Remote action requires explicit approval")
        if action.metadata.get("requires_approval") is True:
            return PolicyResult(PolicyDecision.REQUIRE_APPROVAL, RiskLevel.MUTATING, "Capability explicitly requires approval")
        if self._privileged(text):
            return PolicyResult(PolicyDecision.REQUIRE_APPROVAL, RiskLevel.PRIVILEGED, "Privileged/elevated command")
        if self._destructive(text):
            return PolicyResult(PolicyDecision.REQUIRE_APPROVAL, RiskLevel.DESTRUCTIVE, "Destructive command")
        if self._mutating(text):
            return PolicyResult(PolicyDecision.REQUIRE_APPROVAL, RiskLevel.MUTATING, "Command changes local or remote state")
        if action.metadata.get("read_only") is True and action.metadata.get("capability_id"):
            return PolicyResult(PolicyDecision.ALLOW, RiskLevel.READ_ONLY, "Registered read-only capability")
        if self._read_only(action, text):
            return PolicyResult(PolicyDecision.ALLOW, RiskLevel.READ_ONLY, "Known read-only command family")
        return PolicyResult(PolicyDecision.REQUIRE_APPROVAL, RiskLevel.UNKNOWN, "Unknown action defaults to approval")

    @staticmethod
    def _normalized(action: Action) -> str:
        raw = action.metadata.get("raw_command")
        if isinstance(raw, str) and raw.strip():
            return " ".join(raw.lower().split())
        return " ".join(str(part).lower() for part in action.command)

    @staticmethod
    def _catastrophic(text: str) -> bool:
        patterns = (
            "rm -rf /",
            "rm -fr /",
            "format c:",
            "diskpart clean",
            "del /s /q c:\\*",
            "remove-item c:\\ -recurse",
        )
        return any(pattern in text for pattern in patterns)

    @staticmethod
    def _privileged(text: str) -> bool:
        return text.startswith("sudo ") or " -verb runas" in text or text.startswith("runas ")

    @staticmethod
    def _destructive(text: str) -> bool:
        destructive = (
            "rm -rf ", "rm -fr ", "del /s ", "remove-item ", "rmdir /s ",
            "git reset --hard", "git clean -f", "shutdown ", "taskkill ", "kill -9 ",
        )
        return any(pattern in text for pattern in destructive)

    @staticmethod
    def _mutating(text: str) -> bool:
        prefixes = (
            "git add", "git commit", "git push", "git checkout", "git switch -c", "git branch -d",
            "pip install", "pip uninstall", "npm install", "npm uninstall", "pnpm add", "cargo install",
            "apt install", "apt remove", "winget install", "winget uninstall", "docker rm", "docker stop",
            "mkdir ", "md ", "touch ", "cp ", "copy ", "mv ", "move ", "ren ", "rename-item ",
        )
        if any(text.startswith(prefix) for prefix in prefixes):
            return True
        return any(token in text for token in (" > ", " >> "))

    @staticmethod
    def _read_only(action: Action, text: str) -> bool:
        if action.name in {"git_status", "current_directory", "list_files"}:
            return True
        read_only_prefixes = (
            "git status", "git log", "git diff", "git show", "git branch", "git rev-parse",
            "ls", "dir", "pwd", "cd", "whoami", "where ", "which ", "type ", "cat ",
            "python --version", "python3 --version", "pip --version", "docker ps", "docker images",
            "ollama list", "wsl --list", "wsl.exe --list", "cmd /c dir", "cmd /c cd",
        )
        return any(text == prefix or text.startswith(prefix + " ") for prefix in read_only_prefixes)
