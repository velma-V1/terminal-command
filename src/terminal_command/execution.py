from __future__ import annotations

import shutil
import subprocess
import time

from .contracts import Action, ExecutionResult


def build_wsl_command(command: list[str]) -> list[str]:
    return ["wsl.exe", "--", *command]


class Executor:
    def __init__(self, timeout_s: float = 120.0):
        self.timeout_s = timeout_s

    def execute(self, action: Action) -> ExecutionResult:
        started = time.perf_counter()
        backend = action.backend or "native"

        if backend == "wsl":
            if shutil.which("wsl.exe") is None:
                return self._result(backend, None, "", "WSL is unavailable", started, "unavailable")
            command: str | list[str] = build_wsl_command(action.command)
            shell = False
        elif backend == "native":
            shell = bool(action.metadata.get("shell"))
            if shell:
                command = str(action.metadata.get("raw_command") or (action.command[0] if action.command else ""))
            else:
                command = action.command
        else:
            return self._result(backend, None, "", f"Unknown backend: {backend}", started, "unavailable")

        if not command:
            return self._result(backend, None, "", "No command supplied", started, "error")

        try:
            completed = subprocess.run(
                command,
                cwd=action.cwd,
                capture_output=True,
                text=True,
                timeout=self.timeout_s,
                shell=shell,
                check=False,
            )
        except subprocess.TimeoutExpired as exc:
            return self._result(
                backend,
                None,
                self._text(exc.stdout),
                self._text(exc.stderr) or f"Timed out after {self.timeout_s}s",
                started,
                "timeout",
            )
        except FileNotFoundError as exc:
            return self._result(backend, None, "", str(exc), started, "unavailable")
        except OSError as exc:
            return self._result(backend, None, "", str(exc), started, "error")

        status = "success" if completed.returncode == 0 else "failed"
        return self._result(
            backend,
            completed.returncode,
            completed.stdout,
            completed.stderr,
            started,
            status,
        )

    @staticmethod
    def _text(value: str | bytes | None) -> str:
        if value is None:
            return ""
        if isinstance(value, bytes):
            return value.decode(errors="replace")
        return value

    @staticmethod
    def _result(
        backend: str,
        exit_code: int | None,
        stdout: str,
        stderr: str,
        started: float,
        status: str,
    ) -> ExecutionResult:
        return ExecutionResult(
            backend=backend,
            exit_code=exit_code,
            stdout=stdout,
            stderr=stderr,
            duration_ms=(time.perf_counter() - started) * 1000,
            status=status,
        )
