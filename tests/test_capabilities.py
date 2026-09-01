from __future__ import annotations

import pytest

from terminal_command.capabilities import ArgumentSpec, Capability, CapabilityRegistry
from terminal_command.contracts import Action


def _echo_builder(args):
    return Action(name="demo.echo", command=["echo", args["text"]], metadata={"capability_id": "demo.echo"})


def test_registry_registers_resolves_alias_and_invokes_typed_arguments():
    registry = CapabilityRegistry()
    registry.register(
        Capability(
            id="demo.echo",
            description="Echo text",
            arguments=(ArgumentSpec("text", kind="str"),),
            aliases=("echo text",),
            builder=_echo_builder,
        )
    )

    assert registry.resolve_id("echo text") == "demo.echo"
    action = registry.invoke("demo.echo", {"text": "hello"})
    assert action.command == ["echo", "hello"]
    assert action.metadata["capability_id"] == "demo.echo"


def test_registry_rejects_missing_unknown_and_wrong_typed_arguments():
    registry = CapabilityRegistry()
    registry.register(
        Capability(
            id="demo.limit",
            description="Typed demo",
            arguments=(
                ArgumentSpec("count", kind="int"),
                ArgumentSpec("mode", kind="str", required=False, choices=("fast", "safe")),
            ),
            builder=lambda args: Action(name="demo.limit", command=["echo", str(args["count"])]),
        )
    )

    with pytest.raises(ValueError, match="count"):
        registry.invoke("demo.limit", {})
    with pytest.raises(ValueError, match="count"):
        registry.invoke("demo.limit", {"count": "three"})
    with pytest.raises(ValueError, match="mode"):
        registry.invoke("demo.limit", {"count": 3, "mode": "wild"})
    with pytest.raises(ValueError, match="Unknown argument"):
        registry.invoke("demo.limit", {"count": 3, "extra": True})


def test_registry_lists_stable_capability_metadata_without_builder_objects():
    registry = CapabilityRegistry()
    registry.register(Capability(id="git.status", description="Show Git status", builder=lambda args: Action("git.status", ["git", "status"])))
    rows = registry.describe()
    assert rows == [{"id": "git.status", "description": "Show Git status", "arguments": [], "aliases": []}]
