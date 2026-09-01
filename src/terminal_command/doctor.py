from __future__ import annotations

import platform
import shutil
import sys
from dataclasses import dataclass


@dataclass(frozen=True, slots=True)
class HealthCheck:
    name: str
    available: bool
    detail: str


@dataclass(frozen=True, slots=True)
class DoctorReport:
    core_healthy: bool
    checks: tuple[HealthCheck, ...]


def run_doctor() -> DoctorReport:
    python_ok = sys.version_info >= (3, 11)
    checks = [
        HealthCheck("python", python_ok, platform.python_version()),
        HealthCheck("platform", True, platform.platform()),
        _tool("git", "git"),
        _tool("docker", "docker"),
        _tool("ollama", "ollama"),
        _tool("wsl", "wsl.exe"),
    ]
    return DoctorReport(core_healthy=python_ok, checks=tuple(checks))


def _tool(name: str, executable: str) -> HealthCheck:
    path = shutil.which(executable)
    return HealthCheck(name, path is not None, path or "not found (optional)")
