import os
import sys

from terminal_command.contracts import Action
from terminal_command.execution import Executor, build_wsl_command


def test_build_wsl_command_wraps_structured_command():
    assert build_wsl_command(["python3", "-V"]) == ["wsl.exe", "--", "python3", "-V"]


def test_native_structured_command_captures_output():
    action = Action(
        name="python_marker",
        command=[sys.executable, "-c", "print('terminal-command-ok')"],
    )
    result = Executor().execute(action)
    assert result.exit_code == 0
    assert result.stdout.strip() == "terminal-command-ok"
    assert result.stderr == ""
    assert result.status == "success"


def test_timeout_is_normalized():
    code = "import time; time.sleep(1)"
    action = Action(name="sleep", command=[sys.executable, "-c", code])
    result = Executor(timeout_s=0.01).execute(action)
    assert result.status == "timeout"
    assert result.exit_code is None


def test_wsl_backend_reports_unavailable_when_not_present(monkeypatch):
    monkeypatch.setattr("terminal_command.execution.shutil.which", lambda name: None)
    result = Executor().execute(Action(name="pwd", command=["pwd"], backend="wsl"))
    assert result.status == "unavailable"
    assert result.exit_code is None


def test_explicit_shell_action_uses_raw_command():
    raw = f'"{sys.executable}" -c "print(12345)"'
    action = Action(name="shell", command=[raw], metadata={"raw_command": raw, "shell": True})
    result = Executor().execute(action)
    assert result.exit_code == 0
    assert "12345" in result.stdout
