from __future__ import annotations

import json

from terminal_command.app import AppCore
from terminal_command.capabilities import Capability, CapabilityRegistry
from terminal_command.contracts import Action, InputKind
from terminal_command.model_router import OllamaRouter
from terminal_command.routing import Router


def _transport_with(payload):
    def transport(url, request_payload, timeout):
        return {"message": {"content": json.dumps(payload)}}
    return transport


def _registry():
    registry = CapabilityRegistry()
    registry.register(
        Capability(
            id="git.status",
            description="Show Git status",
            aliases=("show repository status",),
            builder=lambda args: Action(name="git.status", command=["git", "status"], metadata={"capability_id": "git.status"}),
        )
    )
    return registry


def test_model_prefers_registered_capability_over_raw_command():
    registry = _registry()
    router = OllamaRouter(
        "tiny",
        registry=registry,
        transport=_transport_with(
            {
                "capability": "git.status",
                "arguments": {},
                "command": ["rm", "-rf", "/"],
                "confidence": 0.93,
                "explanation": "status requested",
            }
        ),
    )
    route = router.route("show repository status")
    assert route is not None
    assert route.input_kind is InputKind.NATURAL_LANGUAGE
    assert route.action.command == ["git", "status"]
    assert route.action.metadata["capability_id"] == "git.status"
    assert route.confidence == 0.93


def test_model_unknown_capability_is_rejected_instead_of_running_supplied_command():
    router = OllamaRouter(
        "tiny",
        registry=_registry(),
        transport=_transport_with(
            {
                "capability": "system.destroy",
                "arguments": {},
                "command": ["echo", "should-not-run"],
                "confidence": 0.9,
            }
        ),
    )
    assert router.route("do it") is None


def test_model_shell_compatibility_fallback_is_marked_and_policy_gated():
    router = OllamaRouter(
        "tiny",
        registry=_registry(),
        transport=_transport_with(
            {"command": ["git", "log", "-1"], "backend": "native", "confidence": 0.8, "intent": "latest_commit"}
        ),
    )
    route = router.route("latest commit")
    assert route is not None
    assert route.action.metadata["model_proposed"] is True
    assert route.action.metadata["compatibility_fallback"] is True


def test_registered_alias_routes_deterministically_before_model():
    class ExplodingModel:
        def route(self, text):
            raise AssertionError("model should not be called")

    route = Router(model_router=ExplodingModel(), capabilities=_registry()).route("show repository status")
    assert route.source == "capability_alias"
    assert route.action.command == ["git", "status"]
    assert route.rule_id == "capability:git.status"


def test_slash_surfaces_expose_capabilities_and_explain_without_execution(tmp_path):
    app = AppCore(capabilities=_registry())
    caps = app.handle("/capabilities")
    assert caps.status == "info"
    assert "git.status" in caps.message

    explain = app.handle("/explain git status")
    assert explain.status == "info"
    assert "source=shell" in explain.message
    assert "policy=allow" in explain.message
