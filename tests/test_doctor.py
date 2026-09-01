from terminal_command.doctor import run_doctor


def test_doctor_reports_optional_tools_without_failing_core(monkeypatch):
    available = {"git": "/usr/bin/git", "docker": None, "ollama": None, "wsl.exe": None}
    monkeypatch.setattr("terminal_command.doctor.shutil.which", lambda name: available.get(name))
    report = run_doctor()
    checks = {item.name: item for item in report.checks}
    assert report.core_healthy is True
    assert checks["python"].available is True
    assert checks["git"].available is True
    assert checks["docker"].available is False
    assert checks["ollama"].available is False
