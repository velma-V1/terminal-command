from terminal_command.contracts import Action, ExecutionResult, InputKind, RouteResult
from terminal_command.history import HistoryStore, SCHEMA_VERSION
from terminal_command.policy import PolicyEngine


def sample_route():
    return RouteResult(
        input_kind=InputKind.NATURAL_LANGUAGE,
        source="deterministic",
        action=Action(name="git_status", command=["git", "status"]),
        confidence=1.0,
        rule_id="nl.git_status",
    )


def test_history_creates_schema_and_records_event(tmp_path):
    store = HistoryStore(tmp_path / "history.db")
    route = sample_route()
    policy = PolicyEngine().evaluate(route.action)
    execution = ExecutionResult("native", 0, "clean", "", 4.2, "success")

    event_id = store.record("show git status", route, policy, execution)
    rows = store.recent(10)

    assert event_id > 0
    assert len(rows) == 1
    assert rows[0]["request_text"] == "show git status"
    assert rows[0]["route_source"] == "deterministic"
    assert rows[0]["rule_id"] == "nl.git_status"
    assert rows[0]["policy_decision"] == "allow"
    assert rows[0]["risk_level"] == "read_only"
    assert rows[0]["execution_status"] == "success"
    assert rows[0]["action"]["name"] == "git_status"


def test_history_is_newest_first(tmp_path):
    store = HistoryStore(tmp_path / "history.db")
    route = sample_route()
    policy = PolicyEngine().evaluate(route.action)
    store.record("first", route, policy)
    store.record("second", route, policy)
    assert [row["request_text"] for row in store.recent(2)] == ["second", "first"]


def test_schema_version_is_recorded(tmp_path):
    store = HistoryStore(tmp_path / "history.db")
    assert store.schema_version() == SCHEMA_VERSION


def test_history_does_not_persist_process_environment(tmp_path, monkeypatch):
    monkeypatch.setenv("TERMINAL_COMMAND_TEST_SECRET", "do-not-store-me")
    store = HistoryStore(tmp_path / "history.db")
    route = sample_route()
    policy = PolicyEngine().evaluate(route.action)
    store.record("safe request", route, policy)
    raw = (tmp_path / "history.db").read_bytes()
    assert b"do-not-store-me" not in raw
