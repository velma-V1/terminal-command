from __future__ import annotations

import json
import os
import time
import uuid
from dataclasses import asdict, dataclass
from datetime import datetime, timedelta, timezone
from pathlib import Path


@dataclass(slots=True)
class Job:
    id: str
    name: str
    command: list[str]
    interval_seconds: int
    next_run_at: str
    enabled: bool = True
    last_run_at: str | None = None
    last_status: str | None = None


class JobStore:
    def __init__(self, path: str | Path):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._data = self._load()

    def add(
        self,
        name: str,
        command: list[str],
        *,
        interval_seconds: int,
        now: datetime | None = None,
    ) -> Job:
        if not name.strip():
            raise ValueError("Job name cannot be empty")
        if not command or not all(isinstance(part, str) and part for part in command):
            raise ValueError("Job command cannot be empty")
        interval = int(interval_seconds)
        if interval < 1:
            raise ValueError("Job interval must be positive")
        current = now or datetime.now(timezone.utc)
        job = Job(
            id=f"{time.time_ns()}-{uuid.uuid4().hex[:8]}",
            name=name.strip(),
            command=list(command),
            interval_seconds=interval,
            next_run_at=current.isoformat(),
        )
        self._data["jobs"].append(asdict(job))
        self._save()
        return job

    def get(self, job_id: str) -> Job | None:
        for payload in self._data["jobs"]:
            if payload["id"] == job_id:
                return self._job(payload)
        return None

    def list(self) -> list[Job]:
        return [self._job(item) for item in self._data["jobs"]]

    def due(self, *, now: datetime | None = None) -> list[Job]:
        current = now or datetime.now(timezone.utc)
        result = []
        for item in self.list():
            if not item.enabled:
                continue
            scheduled = datetime.fromisoformat(item.next_run_at)
            if scheduled <= current:
                result.append(item)
        return result

    def mark_run(self, job_id: str, *, now: datetime | None = None, status: str) -> Job:
        current = now or datetime.now(timezone.utc)
        payload = self._payload(job_id)
        payload["last_run_at"] = current.isoformat()
        payload["last_status"] = status
        payload["next_run_at"] = (current + timedelta(seconds=int(payload["interval_seconds"]))).isoformat()
        self._save()
        return self._job(payload)

    def enable(self, job_id: str) -> Job:
        payload = self._payload(job_id)
        payload["enabled"] = True
        self._save()
        return self._job(payload)

    def disable(self, job_id: str) -> Job:
        payload = self._payload(job_id)
        payload["enabled"] = False
        self._save()
        return self._job(payload)

    def _payload(self, job_id: str) -> dict:
        for payload in self._data["jobs"]:
            if payload["id"] == job_id:
                return payload
        raise ValueError(f"Unknown job: {job_id}")

    def _load(self) -> dict:
        if not self.path.exists():
            return {"version": 1, "jobs": []}
        try:
            payload = json.loads(self.path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            raise ValueError(f"Invalid job store: {self.path}") from exc
        if payload.get("version") != 1 or not isinstance(payload.get("jobs"), list):
            raise ValueError("Unsupported job store schema")
        return payload

    def _save(self) -> None:
        temp = self.path.with_suffix(self.path.suffix + ".tmp")
        temp.write_text(json.dumps(self._data, indent=2, sort_keys=True), encoding="utf-8")
        os.replace(temp, self.path)

    @staticmethod
    def _job(payload: dict) -> Job:
        return Job(
            id=str(payload["id"]),
            name=str(payload["name"]),
            command=list(payload["command"]),
            interval_seconds=int(payload["interval_seconds"]),
            next_run_at=str(payload["next_run_at"]),
            enabled=bool(payload.get("enabled", True)),
            last_run_at=payload.get("last_run_at"),
            last_status=payload.get("last_status"),
        )
