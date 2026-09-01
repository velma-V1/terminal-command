from __future__ import annotations

import json
import os
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any


@dataclass(slots=True)
class Project:
    name: str
    root: str
    notes: list[str] = field(default_factory=list)
    state: dict[str, Any] = field(default_factory=dict)


class ProjectStore:
    def __init__(self, path: str | Path):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._data = self._load()

    def register(self, root: str | Path, *, name: str | None = None) -> Project:
        resolved = Path(root).expanduser().resolve()
        if not resolved.exists() or not resolved.is_dir():
            raise ValueError(f"Project root does not exist: {resolved}")
        project_name = (name or resolved.name).strip()
        if not project_name:
            raise ValueError("Project name cannot be empty")
        existing = self._data["projects"].get(project_name)
        if existing and Path(existing["root"]).resolve() != resolved:
            raise ValueError(f"Project name already registered: {project_name}")
        if existing:
            return self._project(existing)
        payload = {"name": project_name, "root": str(resolved), "notes": [], "state": {}}
        self._data["projects"][project_name] = payload
        if self._data.get("current") is None:
            self._data["current"] = project_name
        self._save()
        return self._project(payload)

    def discover(self, start: str | Path, *, register: bool = False) -> Project | None:
        current = Path(start).expanduser().resolve()
        if current.is_file():
            current = current.parent
        for candidate in (current, *current.parents):
            if (candidate / ".git").exists():
                if register:
                    return self.register(candidate)
                for payload in self._data["projects"].values():
                    if Path(payload["root"]).resolve() == candidate:
                        return self._project(payload)
                return Project(candidate.name, str(candidate))
        return None

    def get(self, name: str) -> Project | None:
        payload = self._data["projects"].get(name)
        return self._project(payload) if payload else None

    def list(self) -> list[Project]:
        return [self._project(self._data["projects"][name]) for name in sorted(self._data["projects"])]

    def set_current(self, name: str) -> Project:
        project = self.get(name)
        if project is None:
            raise ValueError(f"Unknown project: {name}")
        self._data["current"] = name
        self._save()
        return project

    def current(self) -> Project | None:
        name = self._data.get("current")
        return self.get(name) if name else None

    def add_note(self, name: str, note: str) -> Project:
        if name not in self._data["projects"]:
            raise ValueError(f"Unknown project: {name}")
        clean = note.strip()
        if not clean:
            raise ValueError("Project note cannot be empty")
        self._data["projects"][name]["notes"].append(clean)
        self._save()
        return self.get(name)  # type: ignore[return-value]

    def update_state(self, name: str, key: str, value: Any) -> Project:
        if name not in self._data["projects"]:
            raise ValueError(f"Unknown project: {name}")
        if not key.strip():
            raise ValueError("State key cannot be empty")
        self._data["projects"][name]["state"][key] = value
        self._save()
        return self.get(name)  # type: ignore[return-value]

    def _load(self) -> dict[str, Any]:
        if not self.path.exists():
            return {"version": 1, "current": None, "projects": {}}
        try:
            payload = json.loads(self.path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            raise ValueError(f"Invalid project store: {self.path}") from exc
        if payload.get("version") != 1 or not isinstance(payload.get("projects"), dict):
            raise ValueError("Unsupported project store schema")
        return payload

    def _save(self) -> None:
        temp = self.path.with_suffix(self.path.suffix + ".tmp")
        temp.write_text(json.dumps(self._data, indent=2, sort_keys=True), encoding="utf-8")
        os.replace(temp, self.path)

    @staticmethod
    def _project(payload: dict[str, Any]) -> Project:
        return Project(
            name=str(payload["name"]),
            root=str(payload["root"]),
            notes=list(payload.get("notes", [])),
            state=dict(payload.get("state", {})),
        )
