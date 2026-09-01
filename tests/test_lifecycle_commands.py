from __future__ import annotations

from terminal_command.app import AppCore
from terminal_command.contracts import ExecutionResult
from terminal_command.history import HistoryStore


class FakeExecutor:
    def execute(self, action):
        return ExecutionResult(action.backend, 0, "ok", "", 1.0, "success")


def _app(tmp_path):
    return AppCore(
        history=HistoryStore(tmp_path / "history.db"),
        executor=FakeExecutor(),
        state_dir=tmp_path / "state",
    )


def test_update_without_action_is_local_status_only(tmp_path):
    app = _app(tmp_path)
    outcome = app.handle("/update")
    assert outcome.status == "info"
    assert "manifest" in outcome.message.lower()
    assert "not configured" in outcome.message.lower()


def test_benchmark_slash_runs_deterministic_router_without_executing_actions(tmp_path):
    app = _app(tmp_path)
    outcome = app.handle("/benchmark")
    assert outcome.status == "info"
    assert "deterministic" in outcome.message.lower()
    assert "accuracy=" in outcome.message.lower()
