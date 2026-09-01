from terminal_command.contracts import (
    Action,
    ExecutionResult,
    InputKind,
    PolicyDecision,
    RiskLevel,
    RouteResult,
)


def test_enum_values_are_stable_strings():
    assert InputKind.SHELL.value == "shell"
    assert InputKind.SLASH.value == "slash"
    assert InputKind.NATURAL_LANGUAGE.value == "natural_language"
    assert RiskLevel.READ_ONLY.value == "read_only"
    assert PolicyDecision.ALLOW.value == "allow"
    assert PolicyDecision.REQUIRE_APPROVAL.value == "require_approval"
    assert PolicyDecision.DENY.value == "deny"


def test_action_round_trips_through_dict():
    action = Action(
        name="git_status",
        command=["git", "status"],
        backend="native",
        cwd="C:/repo",
        metadata={"rule_id": "nl.git_status"},
    )
    assert Action.from_dict(action.to_dict()) == action


def test_route_result_serializes_nested_action():
    route = RouteResult(
        input_kind=InputKind.NATURAL_LANGUAGE,
        source="deterministic",
        action=Action(name="pwd", command=["pwd"]),
        confidence=1.0,
        rule_id="nl.pwd",
    )
    payload = route.to_dict()
    assert payload["action"]["name"] == "pwd"
    assert RouteResult.from_dict(payload) == route


def test_execution_result_has_normalized_fields():
    result = ExecutionResult(
        backend="native",
        exit_code=0,
        stdout="ok\n",
        stderr="",
        duration_ms=12.5,
        status="success",
    )
    assert result.to_dict()["status"] == "success"
