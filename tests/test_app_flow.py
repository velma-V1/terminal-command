from terminal_command.app import AppCore
from terminal_command.contracts import ExecutionResult
from terminal_command.history import HistoryStore


class FakeExecutor:
    def __init__(self):
        self.actions = []

    def execute(self, action):
        self.actions.append(action)
        return ExecutionResult(action.backend, 0, "ok", "", 1.0, "success")


def build_app(tmp_path):
    executor = FakeExecutor()
    app = AppCore(history=HistoryStore(tmp_path / "history.db"), executor=executor)
    return app, executor


def test_read_only_natural_language_executes_without_approval(tmp_path):
    app, executor = build_app(tmp_path)
    outcome = app.handle("show git status")
    assert outcome.status == "success"
    assert len(executor.actions) == 1
    assert outcome.policy.decision.value == "allow"
    assert app.history.recent(1)[0]["execution_status"] == "success"


def test_unknown_mutating_shell_command_waits_for_approval(tmp_path):
    app, executor = build_app(tmp_path)
    outcome = app.handle("git commit -m test")
    assert outcome.status == "approval_required"
    assert executor.actions == []
    assert app.history.recent(1)[0]["policy_decision"] == "require_approval"


def test_approved_mutating_command_executes(tmp_path):
    app, executor = build_app(tmp_path)
    outcome = app.handle("git commit -m test", approved=True)
    assert outcome.status == "success"
    assert len(executor.actions) == 1


def test_catastrophic_command_is_denied_even_if_approved(tmp_path):
    app, executor = build_app(tmp_path)
    outcome = app.handle("rm -rf /", approved=True)
    assert outcome.status == "denied"
    assert executor.actions == []


def test_unresolved_language_never_executes(tmp_path):
    app, executor = build_app(tmp_path)
    outcome = app.handle("do the impossible mystery thing")
    assert outcome.status == "unresolved"
    assert executor.actions == []


def test_slash_exit_requests_exit(tmp_path):
    app, _ = build_app(tmp_path)
    outcome = app.handle("/exit")
    assert outcome.exit_requested is True
