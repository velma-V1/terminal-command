from __future__ import annotations

import sys
from pathlib import Path

import pytest

from terminal_command.capabilities import default_capabilities
from terminal_command.packs.daily import register_daily_pack
from terminal_command.policy import PolicyEngine


EXPECTED = {
    "files.search",
    "files.hash",
    "files.duplicates",
    "archive.list",
    "archive.create",
    "system.disk",
    "system.info",
    "launch.url",
    "launch.path",
}


def _registry():
    registry = default_capabilities()
    register_daily_pack(registry)
    return registry


def test_daily_pack_registers_high_value_local_capabilities():
    ids = {row["id"] for row in _registry().describe()}
    assert EXPECTED <= ids


def test_read_only_file_and_system_capabilities_use_internal_ops(tmp_path):
    target = tmp_path / "file.txt"
    target.write_text("hello", encoding="utf-8")
    registry = _registry()

    hash_action = registry.invoke("files.hash", {"path": str(target)})
    duplicate_action = registry.invoke("files.duplicates", {"root": str(tmp_path)})
    disk_action = registry.invoke("system.disk", {"path": str(tmp_path)})
    info_action = registry.invoke("system.info", {})

    for action in (hash_action, duplicate_action, disk_action, info_action):
        assert action.command[:3] == [sys.executable, "-m", "terminal_command.ops"]
        assert action.metadata["read_only"] is True
        assert PolicyEngine().evaluate(action).decision.value == "allow"


def test_search_and_archive_list_are_bounded_read_only_ops(tmp_path):
    root = tmp_path / "root"
    root.mkdir()
    archive = tmp_path / "demo.zip"
    registry = _registry()
    search = registry.invoke("files.search", {"root": str(root), "query": "*.py", "limit": 25})
    listing = registry.invoke("archive.list", {"path": str(archive)})
    assert "25" in search.command
    assert search.metadata["read_only"] is True
    assert listing.metadata["read_only"] is True


def test_archive_create_and_launch_actions_require_approval(tmp_path):
    source = tmp_path / "source"
    source.mkdir()
    registry = _registry()
    archive = registry.invoke("archive.create", {"source": str(source), "output": str(tmp_path / "out.zip")})
    url = registry.invoke("launch.url", {"url": "https://example.com"})
    path = registry.invoke("launch.path", {"path": str(source)})
    policy = PolicyEngine()
    assert policy.evaluate(archive).decision.value == "require_approval"
    assert policy.evaluate(url).decision.value == "require_approval"
    assert policy.evaluate(path).decision.value == "require_approval"


def test_launch_url_rejects_non_http_schemes():
    with pytest.raises(ValueError):
        _registry().invoke("launch.url", {"url": "file:///etc/passwd"})
