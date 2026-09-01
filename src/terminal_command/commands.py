from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True, slots=True)
class SlashCommand:
    name: str
    description: str


class CommandRegistry:
    def __init__(self, commands: list[SlashCommand]):
        self._commands = {command.name: command for command in commands}

    @classmethod
    def default(cls) -> "CommandRegistry":
        return cls(
            [
                SlashCommand("/capabilities", "List registered capability IDs"),
                SlashCommand("/checkpoint", "List or create proven recovery checkpoints"),
                SlashCommand("/doctor", "Check local runtime and optional tools"),
                SlashCommand("/exit", "Exit terminal-command"),
                SlashCommand("/explain", "Explain how a request would route without executing it"),
                SlashCommand("/help", "Show commands and input modes"),
                SlashCommand("/history", "Show recent action evidence"),
                SlashCommand("/project", "Register, select, or inspect resumable projects"),
                SlashCommand("/workflow", "List, inspect, or run saved capability workflows"),
            ]
        )

    def names(self) -> list[str]:
        return sorted(self._commands)

    def resolve(self, text: str) -> SlashCommand | None:
        key = text.strip().split(maxsplit=1)[0].lower()
        return self._commands.get(key)

    def completions(self, prefix: str) -> list[str]:
        lowered = prefix.lower()
        return [name for name in self.names() if name.startswith(lowered)]

    def help_text(self) -> str:
        lines = ["Input modes: normal shell command, natural language, or /command."]
        lines.extend(f"{name:<14} {self._commands[name].description}" for name in self.names())
        return "\n".join(lines)
