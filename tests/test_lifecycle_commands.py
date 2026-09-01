from __future__ import annotations

import hashlib
import json

from terminal_command.app import AppCore
from terminal_command.contracts import ExecutionResult
from terminal_command.history import HistoryStore
from terminal_command.update import switch_current_version


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


def test_update_apply_rejects_tampered_staged_artifact_before_install(tmp_path, monkeypatch):
    install_root = tmp_path / "install"
    current_release = install_root / "releases" / "0.1.0"
    current_release.mkdir(parents=True)
    switch_current_version(install_root, "0.1.0")

    staged = install_root / "staging" / "0.2.0"
    staged.mkdir(parents=True)
    expected = b"original-wheel"
    artifact = staged / "terminal-command-0.2.0.whl"
    artifact.write_bytes(b"tampered-wheel")
    (staged / "manifest.json").write_text(
        json.dumps(
            {
                "version": "0.2.0",
                "artifact_url": "https://example.com/terminal-command-0.2.0.whl",
                "sha256": hashlib.sha256(expected).hexdigest(),
            }
        ),
        encoding="utf-8",
    )

    monkeypatch.setenv("TERMINAL_COMMAND_INSTALL_ROOT", str(install_root))
    monkeypatch.setattr("terminal_command.app.apply_prepared_release", lambda *args, **kwargs: "0.2.0")

    outcome = _app(tmp_path).handle("/update apply 0.2.0", approved=True)

    assert outcome.status == "failed"
    assert "sha256" in outcome.message.lower()
