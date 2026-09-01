from __future__ import annotations

import json

import pytest

from terminal_command.benchmark import BenchmarkCase, load_cases, run_router_benchmark
from terminal_command.capabilities import default_capabilities
from terminal_command.contracts import InputKind, RouteResult
from terminal_command.routing import Router


class FakeModelRouter:
    def __init__(self, registry):
        self.registry = registry
        self.calls = 0

    def route(self, text: str):
        self.calls += 1
        if text == "inspect repository differences":
            action = self.registry.invoke("git.diff", {})
            return RouteResult(
                input_kind=InputKind.NATURAL_LANGUAGE,
                source="model",
                action=action,
                confidence=0.95,
                rule_id="capability:git.diff",
                model_id="fake",
            )
        return None


def test_load_cases_validates_corpus_shape(tmp_path):
    path = tmp_path / "cases.json"
    path.write_text(
        json.dumps(
            [
                {"id": "git-status", "text": "show git status", "expected_capability": "git.status"},
                {"id": "git-diff", "text": "inspect repository differences", "expected_capability": "git.diff"},
            ]
        ),
        encoding="utf-8",
    )
    cases = load_cases(path)
    assert cases[0] == BenchmarkCase("git-status", "show git status", "git.status")


def test_load_cases_rejects_duplicate_ids_and_invalid_rows(tmp_path):
    path = tmp_path / "bad.json"
    path.write_text(
        json.dumps(
            [
                {"id": "same", "text": "one", "expected_capability": "git.status"},
                {"id": "same", "text": "two", "expected_capability": "git.diff"},
            ]
        ),
        encoding="utf-8",
    )
    with pytest.raises(ValueError):
        load_cases(path)


def test_deterministic_and_model_assisted_modes_are_scored_separately():
    registry = default_capabilities()
    cases = [
        BenchmarkCase("status", "show git status", "git.status"),
        BenchmarkCase("diff", "inspect repository differences", "git.diff"),
    ]
    model = FakeModelRouter(registry)

    deterministic = run_router_benchmark(Router(capabilities=registry), cases, mode="deterministic")
    assisted = run_router_benchmark(
        Router(model_router=model, capabilities=registry),
        cases,
        mode="model-assisted",
    )

    assert deterministic.mode == "deterministic"
    assert deterministic.correct == 1
    assert deterministic.total == 2
    assert deterministic.accuracy == 0.5
    assert assisted.mode == "model-assisted"
    assert assisted.correct == 2
    assert assisted.accuracy == 1.0
    assert model.calls == 1


def test_benchmark_records_unresolved_and_wrong_routes():
    registry = default_capabilities()
    cases = [BenchmarkCase("unknown", "completely unknown words", "git.status")]
    result = run_router_benchmark(Router(capabilities=registry), cases, mode="deterministic")
    assert result.correct == 0
    assert result.rows[0].actual_capability is None
    assert result.rows[0].correct is False
