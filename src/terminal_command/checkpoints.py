from __future__ import annotations

import json
import os
import shutil
import subprocess
import time
import uuid
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Callable


@dataclass(slots=True)
class Checkpoint:
    id: str
    kind: str
    label: str
    created_at: str
    project_root: str | None = None
    git_head: str | None = None
    files: list[dict[str, str]] = field(default_factory=list)


GitRunner = Callable[[list[str]], str]


class CheckpointManager:
    def __init__(self, state_dir: str | Path, *, git_runner: GitRunner | None = None):
        self.state_dir = Path(state_dir)
        self.root = self.state_dir / "checkpoints"
        self.root.mkdir(parents=True, exist_ok=True)
        self.index_path = self.root / "index.json"
        self.git_runner = git_runner or self._git_runner
        self._data = self._load()

    def create_files(self, paths: list[str | Path], *, label: str = "") -> Checkpoint:
        if not paths:
            raise ValueError("At least one file is required")
        checkpoint_id = self._new_id()
        backup_root = self.root / checkpoint_id / "files"
        backup_root.mkdir(parents=True, exist_ok=False)
        mappings: list[dict[str, str]] = []
        try:
            for index, raw in enumerate(paths):
                source = Path(raw).expanduser().resolve()
                if not source.exists() or not source.is_file():
                    raise ValueError(f"Checkpoint supports existing files only: {source}")
                backup = backup_root / f"{index:04d}-{source.name}"
                shutil.copy2(source, backup)
                mappings.append({"original": str(source), "backup": str(backup.resolve())})
        except Exception:
            shutil.rmtree(self.root / checkpoint_id, ignore_errors=True)
            raise
        checkpoint = Checkpoint(
            id=checkpoint_id,
            kind="files",
            label=label,
            created_at=self._now(),
            files=mappings,
        )
        self._append(checkpoint)
        return checkpoint

    def create_git(self, repo: str | Path, *, label: str = "") -> Checkpoint:
        root = Path(repo).expanduser().resolve()
        if not root.exists() or not root.is_dir():
            raise ValueError(f"Repository does not exist: {root}")
        head = self.git_runner(["git", "-C", str(root), "rev-parse", "HEAD"]).strip()
        if not head:
            raise ValueError("Could not resolve Git HEAD")
        checkpoint = Checkpoint(
            id=self._new_id(),
            kind="git",
            label=label,
            created_at=self._now(),
            project_root=str(root),
            git_head=head,
        )
        self._append(checkpoint)
        return checkpoint

    def restore_files(self, checkpoint_id: str) -> list[Path]:
        checkpoint = self.get(checkpoint_id)
        if checkpoint is None:
            raise ValueError(f"Unknown checkpoint: {checkpoint_id}")
        if checkpoint.kind != "files":
            raise ValueError("Only explicit file checkpoints can be restored by restore_files")
        restored: list[Path] = []
        for mapping in checkpoint.files:
            original = Path(mapping["original"])
            backup = Path(mapping["backup"])
            if not backup.exists():
                raise ValueError(f"Checkpoint backup missing: {backup}")
            original.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(backup, original)
            restored.append(original.resolve())
        return restored

    def get(self, checkpoint_id: str) -> Checkpoint | None:
        for payload in self._data["checkpoints"]:
            if payload["id"] == checkpoint_id:
                return self._checkpoint(payload)
        return None

    def list(self) -> list[Checkpoint]:
        return [self._checkpoint(payload) for payload in reversed(self._data["checkpoints"])]

    def _append(self, checkpoint: Checkpoint) -> None:
        self._data["checkpoints"].append(asdict(checkpoint))
        self._save()

    def _load(self) -> dict:
        if not self.index_path.exists():
            return {"version": 1, "checkpoints": []}
        try:
            payload = json.loads(self.index_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            raise ValueError(f"Invalid checkpoint index: {self.index_path}") from exc
        if payload.get("version") != 1 or not isinstance(payload.get("checkpoints"), list):
            raise ValueError("Unsupported checkpoint schema")
        return payload

    def _save(self) -> None:
        temp = self.index_path.with_suffix(".tmp")
        temp.write_text(json.dumps(self._data, indent=2, sort_keys=True), encoding="utf-8")
        os.replace(temp, self.index_path)

    @staticmethod
    def _checkpoint(payload: dict) -> Checkpoint:
        return Checkpoint(
            id=str(payload["id"]),
            kind=str(payload["kind"]),
            label=str(payload.get("label", "")),
            created_at=str(payload["created_at"]),
            project_root=payload.get("project_root"),
            git_head=payload.get("git_head"),
            files=[dict(item) for item in payload.get("files", [])],
        )

    @staticmethod
    def _new_id() -> str:
        return f"{time.time_ns()}-{uuid.uuid4().hex[:8]}"

    @staticmethod
    def _now() -> str:
        return datetime.now(timezone.utc).isoformat()

    @staticmethod
    def _git_runner(command: list[str]) -> str:
        return subprocess.check_output(command, text=True, stderr=subprocess.STDOUT)
