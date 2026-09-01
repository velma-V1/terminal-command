from terminal_command.contracts import InputKind
from terminal_command.model_router import OllamaRouter


def test_valid_model_json_becomes_structured_route():
    def transport(url, payload, timeout):
        return {
            "message": {
                "content": '{"intent":"latest_commit","command":["git","log","-1"],"backend":"native","confidence":0.91,"explanation":"read latest commit"}'
            }
        }

    route = OllamaRouter(model="tiny", transport=transport).route("show latest commit")
    assert route.input_kind is InputKind.NATURAL_LANGUAGE
    assert route.source == "model"
    assert route.model_id == "tiny"
    assert route.confidence == 0.91
    assert route.action.command == ["git", "log", "-1"]


def test_model_router_rejects_malformed_json():
    router = OllamaRouter(model="tiny", transport=lambda *args: {"message": {"content": "not-json"}})
    assert router.route("anything") is None


def test_model_router_rejects_unrecognized_backend():
    content = '{"intent":"x","command":["echo","x"],"backend":"root-shell","confidence":1.0}'
    router = OllamaRouter(model="tiny", transport=lambda *args: {"message": {"content": content}})
    assert router.route("anything") is None


def test_model_router_rejects_empty_command():
    content = '{"intent":"x","command":[],"backend":"native","confidence":1.0}'
    router = OllamaRouter(model="tiny", transport=lambda *args: {"message": {"content": content}})
    assert router.route("anything") is None


def test_model_router_failure_is_nonfatal():
    def transport(*args):
        raise TimeoutError("offline")

    assert OllamaRouter(model="tiny", transport=transport).route("anything") is None
