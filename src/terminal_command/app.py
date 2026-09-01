from __future__ import annotations

import argparse
import os
import shlex
from dataclasses import dataclass
from pathlib import Path

from prompt_toolkit import PromptSession
from prompt_toolkit.completion import WordCompleter
from rich.console import Console
from rich.panel import Panel

from .capabilities import CapabilityRegistry, default_capabilities
from .checkpoints import CheckpointManager
from .commands import CommandRegistry
from .contracts import ExecutionResult, PolicyDecision, RouteResult
from .doctor import DoctorReport, run_doctor
from .execution import Executor
from .history import HistoryStore
from .jobs import JobStore
from .model_router import OllamaRouter
from .policy import PolicyEngine, PolicyResult
from .projects import ProjectStore
from .routing import Router
from .workflows import WorkflowRunner, WorkflowStore


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
        projects: ProjectStore | None = None,
        workflows: WorkflowStore | None = None,
        checkpoints: CheckpointManager | None = None,
        jobs: JobStore | None = None,
        state_dir: str | Path | None = None,
        cwd: str | Path | None = None,
    ):
        self.cwd = Path(cwd or Path.cwd()).expanduser().resolve()
        self.capabilities = capabilities or getattr(router, "capabilities", None) or default_capabilities()
        self.capabilities.set_context(cwd=str(self.cwd))
        self.router = router or Router(capabilities=self.capabilities)
        self.policy = policy or PolicyEngine()
        self.executor = executor or Executor()
        self.history = history or HistoryStore(default_history_path())
        base_state = Path(state_dir).expanduser() if state_dir is not None else self.history.path.parent
        base_state.mkdir(parents=True, exist_ok=True)
        self.projects = projects or ProjectStore(base_state / "projects.json")
        self.workflows = workflows or WorkflowStore(base_state / "workflows.json")
        self.checkpoints = checkpoints or CheckpointManager(base_state)
        self.jobs = jobs or JobStore(base_state / "jobs.json")
        self.commands = commands or CommandRegistry.default()

    def handle(self, text: str, *, approved: bool = False) -> AppOutcome:
        self.capabilities.set_context(cwd=str(self.cwd))
        route = self.router.route(text)
        if route.source == "slash":
            return self._handle_slash(text, route, approved=approved)
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

        if self._is_cd_action(route):
            execution = self._execute_cd(route.action.command)
            self.history.record(text, route, policy, execution)
            return AppOutcome(execution.status, execution.stdout or execution.stderr, route, policy, execution)

        if route.action.cwd is None:
            route.action.cwd = str(self.cwd)
        execution = self.executor.execute(route.action)
        self.history.record(text, route, policy, execution)
        return AppOutcome(execution.status, execution.stdout or execution.stderr, route, policy, execution)

    @staticmethod
    def _is_cd_action(route: RouteResult) -> bool:
        if route.source != "shell" or route.action is None or not route.action.command:
            return False
        return route.action.command[0].lower() in {"cd", "chdir"}

    def _execute_cd(self, command: list[str]) -> ExecutionResult:
        if len(command) > 2:
            return ExecutionResult("native", 1, "", "cd accepts one directory", 0.0, "failed")
        raw_target = command[1] if len(command) == 2 else str(Path.home())
        raw_target = raw_target.strip().strip('"').strip("'")
        target = Path(raw_target).expanduser()
        if not target.is_absolute():
            target = self.cwd / target
        try:
            resolved = target.resolve()
        except OSError as exc:
            return ExecutionResult("native", 1, "", str(exc), 0.0, "failed")
        if not resolved.exists() or not resolved.is_dir():
            return ExecutionResult("native", 1, "", f"Directory does not exist: {resolved}", 0.0, "failed")
        self.cwd = resolved
        self.capabilities.set_context(cwd=str(self.cwd))
        return ExecutionResult("native", 0, str(self.cwd), "", 0.0, "success")

    def _handle_slash(self, text: str, route: RouteResult, *, approved: bool) -> AppOutcome:
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
        if command.name == "/project":
            return self._handle_project(text, route)
        if command.name == "/workflow":
            return self._handle_workflow(text, route, approved=approved)
        if command.name == "/checkpoint":
            return self._handle_checkpoint(text, route)
        if command.name == "/jobs":
            return self._handle_jobs(text, route)
        return AppOutcome("unknown_command", f"Unhandled command: {command.name}", route=route)

    def _set_session_project(self, root: str | Path) -> None:
        self.cwd = Path(root).expanduser().resolve()
        self.capabilities.set_context(cwd=str(self.cwd))

    def _handle_project(self, text: str, route: RouteResult) -> AppOutcome:
        try:
            parts = shlex.split(text)
        except ValueError as exc:
            return AppOutcome("error", str(exc), route=route)
        if len(parts) == 1:
            current = self.projects.current()
            rows = self.projects.list()
            if not rows:
                return AppOutcome("info", "No projects registered.", route=route)
            lines = [f"{'*' if current and item.name == current.name else ' '} {item.name} -> {item.root}" for item in rows]
            return AppOutcome("info", "\n".join(lines), route=route)
        action = parts[1].lower()
        try:
            if action == "register" and len(parts) >= 3:
                project = self.projects.register(parts[2], name=parts[3] if len(parts) >= 4 else None)
                self.projects.set_current(project.name)
                self._set_session_project(project.root)
                return AppOutcome("info", f"Current project: {project.name} -> {project.root}", route=route)
            if action == "use" and len(parts) == 3:
                project = self.projects.set_current(parts[2])
                self._set_session_project(project.root)
                return AppOutcome("info", f"Current project: {project.name} -> {project.root}", route=route)
            if action == "note" and len(parts) >= 3:
                current = self.projects.current()
                if current is None:
                    return AppOutcome("error", "No current project.", route=route)
                project = self.projects.add_note(current.name, " ".join(parts[2:]))
                return AppOutcome("info", f"Saved note to {project.name}.", route=route)
        except ValueError as exc:
            return AppOutcome("error", str(exc), route=route)
        return AppOutcome("info", "Usage: /project [register <path> [name] | use <name> | note <text>]", route=route)

    def _handle_workflow(self, text: str, route: RouteResult, *, approved: bool) -> AppOutcome:
        try:
            parts = shlex.split(text)
        except ValueError as exc:
            return AppOutcome("error", str(exc), route=route)
        if len(parts) == 1:
            rows = self.workflows.list()
            if not rows:
                return AppOutcome("info", "No saved workflows.", route=route)
            return AppOutcome("info", "\n".join(f"{item.name:<24} {item.description}" for item in rows), route=route)
        action = parts[1].lower()
        if action == "show" and len(parts) == 3:
            workflow = self.workflows.get(parts[2])
            if workflow is None:
                return AppOutcome("error", f"Unknown workflow: {parts[2]}", route=route)
            lines = [f"{workflow.name}: {workflow.description}"]
            lines.extend(f"- {step.capability_id} {step.arguments}" for step in workflow.steps)
            return AppOutcome("info", "\n".join(lines), route=route)
        if action == "run" and len(parts) == 3:
            workflow = self.workflows.get(parts[2])
            if workflow is None:
                return AppOutcome("error", f"Unknown workflow: {parts[2]}", route=route)
            result = WorkflowRunner(self.capabilities, self.policy, self.executor).run(workflow, approved=approved)
            if result.status == "approval_required":
                return AppOutcome("approval_required", f"Workflow {workflow.name} requires approval.", route=route)
            message = "\n".join(f"{step.capability_id}: {step.status} {step.message}".rstrip() for step in result.steps)
            return AppOutcome(result.status, message, route=route)
        return AppOutcome("info", "Usage: /workflow [show <name> | run <name>]", route=route)

    def _handle_checkpoint(self, text: str, route: RouteResult) -> AppOutcome:
        try:
            parts = shlex.split(text)
        except ValueError as exc:
            return AppOutcome("error", str(exc), route=route)
        if len(parts) == 1:
            rows = self.checkpoints.list()
            if not rows:
                return AppOutcome("info", "No checkpoints.", route=route)
            return AppOutcome(
                "info",
                "\n".join(f"{item.id} {item.kind} {item.label}".rstrip() for item in rows[:20]),
                route=route,
            )
        action = parts[1].lower()
        try:
            if action == "files" and len(parts) >= 3:
                checkpoint = self.checkpoints.create_files([Path(item) for item in parts[2:]], label="manual")
                return AppOutcome("info", f"Created file checkpoint {checkpoint.id}.", route=route)
            if action == "git":
                if len(parts) >= 3:
                    root = Path(parts[2])
                else:
                    current = self.projects.current()
                    root = Path(current.root) if current is not None else self.cwd
                checkpoint = self.checkpoints.create_git(root, label="manual")
                return AppOutcome("info", f"Created Git checkpoint {checkpoint.id} at {checkpoint.git_head}.", route=route)
        except (ValueError, OSError) as exc:
            return AppOutcome("error", str(exc), route=route)
        return AppOutcome("info", "Usage: /checkpoint [files <path...> | git [repo]]", route=route)

    def _handle_jobs(self, text: str, route: RouteResult) -> AppOutcome:
        try:
            parts = shlex.split(text)
        except ValueError as exc:
            return AppOutcome("error", str(exc), route=route)
        if len(parts) == 1:
            rows = self.jobs.list()
            if not rows:
                return AppOutcome("info", "No jobs defined. Jobs are opt-in and are not run by a hidden daemon.", route=route)
            lines = [
                f"{item.id} {'enabled' if item.enabled else 'disabled'} every={item.interval_seconds}s next={item.next_run_at} {item.name}"
                for item in rows
            ]
            return AppOutcome("info", "\n".join(lines), route=route)
        action = parts[1].lower()
        try:
            if action == "add" and len(parts) >= 5:
                interval = int(parts[3])
                job = self.jobs.add(parts[2], parts[4:], interval_seconds=interval)
                return AppOutcome("info", f"Added job {job.id}. No daemon was installed; run due jobs explicitly.", route=route)
            if action == "enable" and len(parts) == 3:
                job = self.jobs.enable(parts[2])
                return AppOutcome("info", f"Enabled job {job.id}.", route=route)
            if action == "disable" and len(parts) == 3:
                job = self.jobs.disable(parts[2])
                return AppOutcome("info", f"Disabled job {job.id}.", route=route)
            if action == "due" and len(parts) == 2:
                rows = self.jobs.due()
                if not rows:
                    return AppOutcome("info", "No jobs are due.", route=route)
                return AppOutcome("info", "\n".join(f"{item.id} {item.name}: {' '.join(item.command)}" for item in rows), route=route)
        except (ValueError, OSError) as exc:
            return AppOutcome("error", str(exc), route=route)
        return AppOutcome("info", "Usage: /jobs [add <name> <interval-seconds> <command...> | enable <id> | disable <id> | due]", route=route)


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
