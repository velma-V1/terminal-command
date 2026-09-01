from __future__ import annotations

import os
from pathlib import Path

import pytest

from terminal_command.capabilities import CapabilityRegistry
from terminal_command.packs import security
from terminal_command.packs.security import register_security_pack
from terminal_command.policy import PolicyEngine


EXPECTED = {
    "security.secrets",
    "security.deps_audit",
    "security.static_scan",
    "security.network",
}


def _registry() -> CapabilityRegistry:
    registry = CapabilityRegistry()
    register_security_pack(registry)
    return registry


def test_security_pack_registers_bounded_defensive_capabilities():
    ids = {row["id"] for row in _registry().describe()}
    assert EXPECTED <= ids


def test_secret_scan_prefers_gitleaks_when_available(monkeypatch, tmp_path):
    monkeypatch.setattr(security.shutil, "which", lambda name: "/tools/gitleaks" if name == "gitleaks" else None)
    action = _registry().invoke("security.secrets", {"root": str(tmp_path)})
    assert action.command[0] == "/tools/gitleaks"
    assert action.metadata["security"] is True
    assert action.metadata["tool"] == "gitleaks"
    assert PolicyEngine().evaluate(action).decision.value == "require_approval"


def test_secret_scan_has_local_degraded_fallback(monkeypatch, tmp_path):
    monkeypatch.setattr(security.shutil, "which", lambda name: None)
    action = _registry().invoke("security.secrets", {"root": str(tmp_path)})
    assert "terminal_command.security_ops" in action.command
    assert action.metadata["degraded"] is True
    assert action.metadata["security"] is True


def test_dependency_audit_discovers_python_auditor(monkeypatch, tmp_path):
    (tmp_path / "pyproject.toml").write_text("[project]\nname='demo'\n", encoding="utf-8")
    monkeypatch.setattr(security.shutil, "which", lambda name: "/tools/pip-audit" if name == "pip-audit" else None)
    action = _registry().invoke("security.deps_audit", {"root": str(tmp_path)})
    assert action.command[0] == "/tools/pip-audit"
    assert action.cwd == str(tmp_path.resolve())
    assert action.metadata["security"] is True


def test_static_scan_prefers_semgrep_with_explicit_config(monkeypatch, tmp_path):
    config = tmp_path / "rules.yml"
    config.write_text("rules: []\n", encoding="utf-8")
    monkeypatch.setattr(security.shutil, "which", lambda name: "/tools/semgrep" if name == "semgrep" else None)
    action = _registry().invoke(
        "security.static_scan",
        {"root": str(tmp_path), "config": str(config)},
    )
    assert action.command[:4] == ["/tools/semgrep", "scan", "--config", str(config.resolve())]
    assert action.metadata["security"] is True


def test_network_inspection_is_local_and_approval_gated(monkeypatch):
    monkeypatch.setattr(security.shutil, "which", lambda name: "/usr/bin/ss" if name == "ss" else None)
    action = _registry().invoke("security.network", {})
    if os.name == "nt":
        assert action.command == ["netstat", "-ano"]
    else:
        assert action.command == ["/usr/bin/ss", "-lntup"]
    assert action.metadata["security"] is True
    assert PolicyEngine().evaluate(action).decision.value == "require_approval"


def test_security_root_must_exist(tmp_path):
    with pytest.raises(ValueError):
        _registry().invoke("security.secrets", {"root": str(tmp_path / "missing")})
