from __future__ import annotations

from pathlib import Path

from terminal_command.app import AppCore
from terminal_command.contracts import ExecutionResult
from terminal_command.history import HistoryStore


class FakeExecutor:
    def __init__(self):
        self.actions = []

    def execute(self, action):
        self.actions.append(action)
        return ExecutionResult(action.backend, 0, "ok", "", 1.0, "success")


def test_cd_updates_session_working_directory_without_spawning_command(tmp_path):
    target = tmp_path / "project"
    target.mkdir()
    executor = FakeExecutor()
    app = AppCore(history=HistoryStore(tmp_path / "history.db"), executor=executor, state_dir=tmp_path / "state")

    outcome = app.handle(f'cd "{target}"')
    assert outcome.status == "success"
    assert app.cwd == target.resolve()
    assert executor.actions == []

    outcome = app.handle("git status")
    assert outcome.status == "success"
    assert executor.actions[-1].cwd == str(target.resolve())


def test_cd_rejects_missing_directory_and_preserves_current_cwd(tmp_path):
    executor = FakeExecutor()
    app = AppCore(history=HistoryStore(tmp_path / "history.db"), executor=executor, state_dir=tmp_path / "state")
    original = app.cwd
    outcome = app.handle(f"cd {tmp_path / 'missing'}")
    assert outcome.status == "failed"
    assert app.cwd == original
    assert executor.actions == []
