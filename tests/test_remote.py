from __future__ import annotations

import pytest

from terminal_command.policy import PolicyEngine
from terminal_command.remote import build_ssh_action, validate_ssh_target


def test_validate_ssh_target_accepts_host_user_and_ip_forms():
    assert validate_ssh_target("server") == "server"
    assert validate_ssh_target("user@example.com") == "user@example.com"
    assert validate_ssh_target("192.168.1.10") == "192.168.1.10"
    assert validate_ssh_target("user@[2001:db8::1]") == "user@[2001:db8::1]"


def test_validate_ssh_target_rejects_option_and_shell_injection_shapes():
    for value in (
        "-oProxyCommand=evil",
        "user@host;rm -rf /",
        "user@host && whoami",
        "bad host",
        "user@host/path",
        "",
    ):
        with pytest.raises(ValueError):
            validate_ssh_target(value)


def test_build_ssh_action_is_structured_and_mandatory_approval():
    action = build_ssh_action("user@example.com", ["uptime"])
    assert action.command == ["ssh", "user@example.com", "uptime"]
    assert action.metadata["remote"] is True
    assert action.metadata["requires_approval"] is True
    assert action.metadata["transport"] == "ssh"
    assert PolicyEngine().evaluate(action).decision.value == "require_approval"


def test_tailscale_ssh_uses_explicit_transport_without_credentials():
    action = build_ssh_action("node.tailnet.ts.net", ["hostname"], use_tailscale=True)
    assert action.command == ["tailscale", "ssh", "node.tailnet.ts.net", "hostname"]
    assert action.metadata["transport"] == "tailscale-ssh"
    assert all("password" not in part.lower() for part in action.command)


def test_remote_command_requires_nonempty_argv():
    with pytest.raises(ValueError):
        build_ssh_action("server", [])
