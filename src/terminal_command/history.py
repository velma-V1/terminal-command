from __future__ import annotations

import json
import sqlite3
from datetime import datetime, timezone
from pathlib import Path

from .contracts import ExecutionResult, RouteResult
from .policy import PolicyResult

SCHEMA_VERSION = 1


class HistoryStore:
    def __init__(self, path: str | Path):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._ensure_schema()

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.path)
        connection.row_factory = sqlite3.Row
        return connection

    def _ensure_schema(self) -> None:
        with self._connect() as connection:
            connection.execute(
                "CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL)"
            )
            connection.execute(
                """
                CREATE TABLE IF NOT EXISTS events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    created_at TEXT NOT NULL,
                    request_text TEXT NOT NULL,
                    input_kind TEXT NOT NULL,
                    route_source TEXT NOT NULL,
                    rule_id TEXT,
                    model_id TEXT,
                    confidence REAL,
                    action_json TEXT,
                    policy_decision TEXT NOT NULL,
                    risk_level TEXT NOT NULL,
                    policy_reason TEXT NOT NULL,
                    backend TEXT,
                    execution_status TEXT,
                    exit_code INTEGER,
                    duration_ms REAL,
                    execution_json TEXT,
                    error TEXT
                )
                """
            )
            connection.execute(
                "INSERT OR IGNORE INTO schema_migrations(version, applied_at) VALUES (?, ?)",
                (SCHEMA_VERSION, self._now()),
            )

    def schema_version(self) -> int:
        with self._connect() as connection:
            row = connection.execute("SELECT MAX(version) AS version FROM schema_migrations").fetchone()
        return int(row["version"] or 0)

    def record(
        self,
        request_text: str,
        route: RouteResult,
        policy: PolicyResult,
        execution: ExecutionResult | None = None,
        *,
        error: str | None = None,
    ) -> int:
        action_json = json.dumps(route.action.to_dict(), sort_keys=True) if route.action else None
        execution_json = json.dumps(execution.to_dict(), sort_keys=True) if execution else None
        with self._connect() as connection:
            cursor = connection.execute(
                """
                INSERT INTO events(
                    created_at, request_text, input_kind, route_source, rule_id, model_id,
                    confidence, action_json, policy_decision, risk_level, policy_reason,
                    backend, execution_status, exit_code, duration_ms, execution_json, error
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    self._now(),
                    request_text,
                    route.input_kind.value,
                    route.source,
                    route.rule_id,
                    route.model_id,
                    route.confidence,
                    action_json,
                    policy.decision.value,
                    policy.risk.value,
                    policy.reason,
                    execution.backend if execution else (route.action.backend if route.action else None),
                    execution.status if execution else None,
                    execution.exit_code if execution else None,
                    execution.duration_ms if execution else None,
                    execution_json,
                    error,
                ),
            )
            return int(cursor.lastrowid)

    def recent(self, limit: int = 20) -> list[dict]:
        safe_limit = max(1, min(int(limit), 1000))
        with self._connect() as connection:
            rows = connection.execute(
                "SELECT * FROM events ORDER BY id DESC LIMIT ?", (safe_limit,)
            ).fetchall()
        results: list[dict] = []
        for row in rows:
            item = dict(row)
            item["action"] = json.loads(item.pop("action_json")) if item.get("action_json") else None
            item["execution"] = json.loads(item.pop("execution_json")) if item.get("execution_json") else None
            results.append(item)
        return results

    @staticmethod
    def _now() -> str:
        return datetime.now(timezone.utc).isoformat()
