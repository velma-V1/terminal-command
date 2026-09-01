from __future__ import annotations

import json
import os
from dataclasses import asdict, dataclass
from pathlib import Path
from urllib.parse import urlsplit

CURRENT_CONFIG_VERSION = 1
_VALID_CHANNELS = {"stable", "development"}


@dataclass(slots=True)
class AppConfig:
    version: int = CURRENT_CONFIG_VERSION
    model_enabled: bool = True
    model: str = "qwen3.5:2b"
    update_channel: str = "stable"
    update_manifest_url: str | None = None


class ConfigStore:
    def __init__(self, path: str | Path):
        self.path = Path(path).expanduser()

    def load(self) -> AppConfig:
        if not self.path.exists():
            config = AppConfig()
            self.save(config)
            return config
        try:
            payload = json.loads(self.path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            raise ValueError(f"Invalid config file: {self.path}: {exc}") from exc
        if not isinstance(payload, dict):
            raise ValueError("Config root must be an object")
        version = payload.get("version", 0)
        if not isinstance(version, int) or isinstance(version, bool) or version < 0:
            raise ValueError("Invalid config version")
        if version > CURRENT_CONFIG_VERSION:
            raise ValueError(
                f"Config version {version} is newer than supported version {CURRENT_CONFIG_VERSION}"
            )
        migrated = version < CURRENT_CONFIG_VERSION
        payload = self._migrate(payload, version)
        config = self._from_payload(payload)
        if migrated:
            self.save(config)
        return config

    def save(self, config: AppConfig) -> None:
        self._validate(config)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        temp = self.path.with_name(self.path.name + ".tmp")
        temp.write_text(json.dumps(asdict(config), indent=2, sort_keys=True) + "\n", encoding="utf-8")
        os.replace(temp, self.path)

    @staticmethod
    def _migrate(payload: dict, version: int) -> dict:
        current = dict(payload)
        if version == 0:
            current.setdefault("model_enabled", True)
            current.setdefault("model", "qwen3.5:2b")
            current.setdefault("update_channel", "stable")
            current.setdefault("update_manifest_url", None)
            current["version"] = 1
        return current

    @staticmethod
    def _from_payload(payload: dict) -> AppConfig:
        config = AppConfig(
            version=int(payload.get("version", CURRENT_CONFIG_VERSION)),
            model_enabled=payload.get("model_enabled", True),
            model=payload.get("model", "qwen3.5:2b"),
            update_channel=payload.get("update_channel", "stable"),
            update_manifest_url=payload.get("update_manifest_url"),
        )
        ConfigStore._validate(config)
        return config

    @staticmethod
    def _validate(config: AppConfig) -> None:
        if config.version != CURRENT_CONFIG_VERSION:
            raise ValueError(f"Config must use schema version {CURRENT_CONFIG_VERSION}")
        if not isinstance(config.model_enabled, bool):
            raise ValueError("model_enabled must be boolean")
        if not isinstance(config.model, str) or not config.model.strip():
            raise ValueError("model must be a nonempty string")
        if config.update_channel not in _VALID_CHANNELS:
            raise ValueError(f"update_channel must be one of {sorted(_VALID_CHANNELS)}")
        if config.update_manifest_url is not None:
            if not isinstance(config.update_manifest_url, str):
                raise ValueError("update_manifest_url must be a string or null")
            parsed = urlsplit(config.update_manifest_url)
            if parsed.scheme.lower() != "https" or not parsed.hostname:
                raise ValueError("update_manifest_url must be an https URL")
            if parsed.username is not None or parsed.password is not None:
                raise ValueError("update_manifest_url must not contain credentials")
