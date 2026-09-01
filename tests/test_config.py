from __future__ import annotations

import json

import pytest

from terminal_command.config import CURRENT_CONFIG_VERSION, ConfigStore


def test_missing_config_loads_versioned_defaults_and_persists(tmp_path):
    path = tmp_path / "config.json"
    store = ConfigStore(path)
    config = store.load()

    assert config.version == CURRENT_CONFIG_VERSION
    assert config.model_enabled is True
    assert config.model == "qwen3.5:2b"
    assert config.update_channel == "stable"
    assert config.update_manifest_url is None
    assert json.loads(path.read_text(encoding="utf-8"))["version"] == CURRENT_CONFIG_VERSION


def test_version_zero_config_migrates_without_losing_user_model_choice(tmp_path):
    path = tmp_path / "config.json"
    path.write_text(json.dumps({"version": 0, "model": "tiny:2b", "model_enabled": False}), encoding="utf-8")

    config = ConfigStore(path).load()

    assert config.version == CURRENT_CONFIG_VERSION
    assert config.model == "tiny:2b"
    assert config.model_enabled is False
    assert config.update_channel == "stable"
    assert config.update_manifest_url is None
    assert json.loads(path.read_text(encoding="utf-8"))["version"] == CURRENT_CONFIG_VERSION


def test_config_save_round_trips_and_uses_known_schema(tmp_path):
    store = ConfigStore(tmp_path / "config.json")
    config = store.load()
    config.update_channel = "development"
    config.update_manifest_url = "https://example.com/terminal-command-manifest.json"
    store.save(config)

    loaded = ConfigStore(store.path).load()
    assert loaded.update_channel == "development"
    assert loaded.update_manifest_url == "https://example.com/terminal-command-manifest.json"


def test_future_config_version_is_rejected(tmp_path):
    path = tmp_path / "config.json"
    path.write_text(json.dumps({"version": CURRENT_CONFIG_VERSION + 1}), encoding="utf-8")
    with pytest.raises(ValueError, match="newer"):
        ConfigStore(path).load()
