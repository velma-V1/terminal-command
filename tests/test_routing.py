from terminal_command.contracts import InputKind
from terminal_command.routing import Router


class FakeModelRouter:
    def __init__(self, result=None):
        self.result = result
        self.calls = []

    def route(self, text):
        self.calls.append(text)
        return self.result


def test_slash_command_has_highest_precedence():
    route = Router().route("/doctor")
    assert route.input_kind is InputKind.SLASH
    assert route.source == "slash"
    assert route.action.name == "slash.doctor"


def test_obvious_shell_command_bypasses_model():
    model = FakeModelRouter()
    route = Router(model_router=model).route("git status")
    assert route.input_kind is InputKind.SHELL
    assert route.source == "shell"
    assert route.action.command == ["git", "status"]
    assert model.calls == []


def test_deterministic_natural_language_rule_runs_before_model():
    model = FakeModelRouter()
    route = Router(model_router=model).route("show git status")
    assert route.input_kind is InputKind.NATURAL_LANGUAGE
    assert route.source == "deterministic"
    assert route.rule_id == "nl.git_status"
    assert route.action.command == ["git", "status"]
    assert model.calls == []


def test_unknown_language_uses_optional_model_router():
    from terminal_command.contracts import Action, RouteResult

    expected = RouteResult(
        input_kind=InputKind.NATURAL_LANGUAGE,
        source="model",
        action=Action(name="shell", command=["git", "log", "-1"]),
        confidence=0.9,
        model_id="tiny",
    )
    model = FakeModelRouter(expected)
    route = Router(model_router=model).route("show me the latest commit")
    assert route == expected
    assert model.calls == ["show me the latest commit"]


def test_unknown_language_is_safe_unresolved_without_model():
    route = Router().route("do the weird thing I mentioned last week")
    assert route.input_kind is InputKind.UNRESOLVED
    assert route.source == "unresolved"
    assert route.action is None
