from __future__ import annotations

import argparse
import os
from dataclasses import dataclass
from pathlib import Path

from prompt_toolkit import PromptSession
from prompt_toolkit.completion import WordCompleter
from rich.console import Console
from rich.panel import Panel

from .capabilities import CapabilityRegistry, default_capabilities
from .commands import CommandRegistry
from .contracts import ExecutionResult, PolicyDecision, RouteResult
from .doctor import DoctorReport, run_doctor
from .execution import Executor
from .history import HistoryStore
from .model_router import OllamaRouter
from .policy import PolicyEngine, PolicyResult
from .routing import Router


@dataclass(slots=True)
class AppOutcome:
    status: str
    message: str = ""
    route: RouteResult | None = None
    policy: PolicyResult | None = None
    execution: ExecutionResult | None = None
    exit_requested: bool = False


class AppCore:
    def __init__(
        self,
        *,
        router: Router | None = None,
        policy: PolicyEngine | None = None,
        executor: Executor | None = None,
        history: HistoryStore | None = None,
        commands: CommandRegistry | None = None,
        capabilities: CapabilityRegistry | None = None,
    ):
        self.capabilities = capabilities or getattr(router, "capabilities", None) or default_capabilities()
        self.router = router or Router(capabilities=self.capabilities)
        self.policy = policy or PolicyEngine()
        self.executor = executor or Executor()
        self.history = history or HistoryStore(default_history_path())
        self.commands = commands or CommandRegistry.default()

    def handle(self, text: str, *, approved: bool = False) -> AppOutcome:
        route = self.router.route(text)
        if route.source == "slash":
            return self._handle_slash(text, route)
        if route.action is None:
            return AppOutcome(
                status="unresolved",
                message=route.explanation or "I could not safely resolve that request.",
                route=route,
            )

        policy = self.policy.evaluate(route.action)
        if policy.decision is PolicyDecision.DENY:
            self.history.record(text, route, policy, error="denied")
            return AppOutcome("denied", policy.reason, route, policy)
        if policy.decision is PolicyDecision.REQUIRE_APPROVAL and not approved:
            self.history.record(text, route, policy, error="approval_required")
            return AppOutcome("approval_required", policy.reason, route, policy)

        execution = self.executor.execute(route.action)
        self.history.record(text, route, policy, execution)
        return AppOutcome(execution.status, execution.stdout or execution.stderr, route, policy, execution)

    def _handle_slash(self, text: str, route: RouteResult) -> AppOutcome:
        command = self.commands.resolve(text)
        if command is None:
            return AppOutcome("unknown_command", f"Unknown command: {text}", route=route)
        if command.name == "/exit":
            return AppOutcome("exit", "", route=route, exit_requested=True)
        if command.name == "/help":
            return AppOutcome("info", self.commands.help_text(), route=route)
        if command.name == "/doctor":
            return AppOutcome("info", format_doctor(run_doctor()), route=route)
        if command.name == "/history":
            rows = self.history.recent(20)
            if not rows:
                return AppOutcome("info", "No recorded actions yet.", route=route)
            lines = []
            for row in rows:
                outcome = row.get("execution_status") or row.get("error") or row.get("policy_decision")
                lines.append(f"#{row['id']} {row['request_text']} -> {outcome}")
            return AppOutcome("info", "\n".join(lines), route=route)
        if command.name == "/capabilities":
            rows = self.capabilities.describe()
            if not rows:
                return AppOutcome("info", "No capabilities registered.", route=route)
            return AppOutcome(
                "info",
                "\n".join(f"{row['id']:<24} {row['description']}" for row in rows),
                route=route,
            )
        if command.name == "/explain":
            parts = text.strip().split(maxsplit=1)
            if len(parts) == 1 or not parts[1].strip():
                return AppOutcome("info", "Usage: /explain <request>", route=route)
            proposed = self.router.route(parts[1])
            if proposed.action is None:
                return AppOutcome("info", f"source={proposed.source} unresolved", route=route)
            policy = self.policy.evaluate(proposed.action)
            capability_id = proposed.action.metadata.get("capability_id", "-")
            preview = proposed.action.metadata.get("raw_command") or " ".join(proposed.action.command)
            message = (
                f"source={proposed.source} rule={proposed.rule_id or '-'} capability={capability_id} "
                f"policy={policy.decision.value} risk={policy.risk.value} command={preview}"
            )
            return AppOutcome("info", message, route=route)
        return AppOutcome("unknown_command", f"Unhandled command: {command.name}", route=route)


def default_history_path() -> Path:
    configured = os.environ.get("TERMINAL_COMMAND_HISTORY")
    if configured:
        return Path(configured).expanduser()
    return Path.home() / ".terminal-command" / "history.db"


def format_doctor(report: DoctorReport) -> str:
    lines = [f"core: {'ok' if report.core_healthy else 'failed'}"]
    for check in report.checks:
        marker = "ok" if check.available else "optional-missing"
        lines.append(f"{check.name}: {marker} ({check.detail})")
    return "\n".join(lines)


def _preview(outcome: AppOutcome) -> str:
    if outcome.route and outcome.route.action:
        action = outcome.route.action
        raw = action.metadata.get("raw_command")
        return str(raw or " ".join(action.command))
    return ""


def _print_outcome(console: Console, outcome: AppOutcome) -> None:
    if outcome.execution:
        if outcome.execution.stdout:
            console.print(outcome.execution.stdout.rstrip())
        if outcome.execution.stderr:
            console.print(outcome.execution.stderr.rstrip(), style="yellow")
        if outcome.execution.status not in {"success", "failed"}:
            console.print(f"[{outcome.execution.status}]")
    elif outcome.message:
        console.print(outcome.message)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="terminal-command")
    parser.add_argument("--doctor", action="store_true", help="Run health checks and exit")
    parser.add_argument("--no-model", action="store_true", help="Disable Ollama intent routing")
    parser.add_argument("--model", default=os.environ.get("TERMINAL_COMMAND_MODEL", "qwen3.5:2b"))
    parser.add_argument("--history-db", default=None)
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    console = Console()
    if args.doctor:
        console.print(format_doctor(run_doctor()))
        return 0

    history = HistoryStore(Path(args.history_db).expanduser()) if args.history_db else HistoryStore(default_history_path())
    capabilities = default_capabilities()
    model_router = None if args.no_model else OllamaRouter(model=args.model, registry=capabilities)
    app = AppCore(
        router=Router(model_router=model_router, capabilities=capabilities),
        history=history,
        capabilities=capabilities,
    )

    console.print(Panel.fit("TERMINAL COMMAND\nNatural language • shell • /commands", border_style="cyan"))
    registry = app.commands
    completer = WordCompleter(registry.names(), sentence=True)
    session = PromptSession(completer=completer, complete_while_typing=True, mouse_support=True)

    while True:
        try:
            text = session.prompt("> ")
        except (EOFError, KeyboardInterrupt):
            console.print()
            return 0
        if not text.strip():
            continue

        outcome = app.handle(text)
        if outcome.exit_requested:
            return 0
        if outcome.status == "approval_required":
            console.print(f"Approval required: {_preview(outcome)}", style="yellow")
            try:
                answer = session.prompt("Execute? [y/N] ").strip().lower()
            except (EOFError, KeyboardInterrupt):
                console.print()
                continue
            if answer in {"y", "yes"}:
                outcome = app.handle(text, approved=True)
            else:
                console.print("Cancelled.")
                continue
        _print_outcome(console, outcome)


if __name__ == "__main__":
    raise SystemExit(main())
