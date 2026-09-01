from terminal_command.contracts import Action, ExecutionResult, InputKind, RouteResult
from terminal_command.history import HistoryStore
from terminal_command.policy import PolicyEngine


REQUIRED_FIELDS = {
    "input_kind",
    "route_source",
    "rule_id",
    "model_id",
    "confidence",
    "policy_decision",
    "risk_level",
    "backend",
    "execution_status",
    "duration_ms",
    "exit_code",
}


def test_history_preserves_experimental_routing_and_execution_fields(tmp_path):
    store = HistoryStore(tmp_path / "history.db")
    route = RouteResult(
        input_kind=InputKind.NATURAL_LANGUAGE,
        source="model",
        action=Action(name="git_status", command=["git", "status"]),
        confidence=0.88,
        model_id="tiny-test-model",
    )
    policy = PolicyEngine().evaluate(route.action)
    execution = ExecutionResult("native", 0, "ok", "", 3.5, "success")
    store.record("check the repo", route, policy, execution)

    row = store.recent(1)[0]
    assert REQUIRED_FIELDS <= set(row)
    assert row["route_source"] == "model"
    assert row["model_id"] == "tiny-test-model"
    assert row["confidence"] == 0.88
    assert row["backend"] == "native"
    assert row["duration_ms"] == 3.5
