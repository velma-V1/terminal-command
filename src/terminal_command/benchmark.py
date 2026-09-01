from __future__ import annotations

import argparse
import json
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Iterable

from .routing import Router


@dataclass(frozen=True, slots=True)
class BenchmarkCase:
    id: str
    text: str
    expected_capability: str


@dataclass(frozen=True, slots=True)
class BenchmarkRow:
    id: str
    text: str
    expected_capability: str
    actual_capability: str | None
    source: str
    correct: bool


@dataclass(frozen=True, slots=True)
class BenchmarkResult:
    mode: str
    correct: int
    total: int
    accuracy: float
    rows: tuple[BenchmarkRow, ...]

    def to_dict(self) -> dict:
        return {
            "mode": self.mode,
            "correct": self.correct,
            "total": self.total,
            "accuracy": self.accuracy,
            "rows": [asdict(row) for row in self.rows],
        }


def load_cases(path: str | Path) -> list[BenchmarkCase]:
    source = Path(path)
    try:
        payload = json.loads(source.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ValueError(f"Invalid benchmark corpus: {source}: {exc}") from exc
    if not isinstance(payload, list) or not payload:
        raise ValueError("Benchmark corpus must be a nonempty JSON array")
    cases: list[BenchmarkCase] = []
    seen: set[str] = set()
    for row in payload:
        if not isinstance(row, dict):
            raise ValueError("Benchmark rows must be objects")
        case_id = row.get("id")
        text = row.get("text")
        expected = row.get("expected_capability")
        if not all(isinstance(value, str) and value.strip() for value in (case_id, text, expected)):
            raise ValueError("Benchmark rows require id, text, and expected_capability strings")
        case_id = case_id.strip()
        if case_id in seen:
            raise ValueError(f"Duplicate benchmark id: {case_id}")
        seen.add(case_id)
        cases.append(BenchmarkCase(case_id, text.strip(), expected.strip()))
    return cases


def default_cases() -> list[BenchmarkCase]:
    return [
        BenchmarkCase("git-status-1", "show git status", "git.status"),
        BenchmarkCase("git-status-2", "what changed in git", "git.status"),
        BenchmarkCase("cwd", "where am i", "system.cwd"),
        BenchmarkCase("files", "show files here", "files.list"),
        BenchmarkCase("git-diff", "inspect repository differences", "git.diff"),
        BenchmarkCase("git-log", "show recent repository commits", "git.log"),
        BenchmarkCase("processes", "inspect running processes", "process.inspect"),
        BenchmarkCase("system", "show detailed system information", "system.info"),
    ]


def run_router_benchmark(router: Router, cases: Iterable[BenchmarkCase], *, mode: str) -> BenchmarkResult:
    if mode not in {"deterministic", "model-assisted"}:
        raise ValueError("mode must be deterministic or model-assisted")
    rows: list[BenchmarkRow] = []
    for case in cases:
        route = router.route(case.text)
        actual: str | None = None
        if route.action is not None:
            value = route.action.metadata.get("capability_id")
            if isinstance(value, str):
                actual = value
        rows.append(
            BenchmarkRow(
                id=case.id,
                text=case.text,
                expected_capability=case.expected_capability,
                actual_capability=actual,
                source=route.source,
                correct=actual == case.expected_capability,
            )
        )
    total = len(rows)
    correct = sum(1 for row in rows if row.correct)
    return BenchmarkResult(mode, correct, total, (correct / total) if total else 0.0, tuple(rows))


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="python -m terminal_command.benchmark")
    parser.add_argument("corpus", nargs="?")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    cases = load_cases(args.corpus) if args.corpus else default_cases()
    result = run_router_benchmark(Router(), cases, mode="deterministic")
    print(json.dumps(result.to_dict(), indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
