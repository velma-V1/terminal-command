from __future__ import annotations

import ipaddress
import re
from typing import Any

from .capabilities import ArgumentSpec, Capability, CapabilityRegistry
from .contracts import Action

_USER_RE = re.compile(r"^[A-Za-z0-9._-]{1,64}$")
_HOST_RE = re.compile(r"^(?=.{1,253}$)(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)(?:\.(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?))*$", re.IGNORECASE)


def validate_ssh_target(target: str) -> str:
    value = str(target).strip()
    if not value or len(value) > 320:
        raise ValueError("Invalid SSH target")
    if value.startswith("-") or any(ch.isspace() or ord(ch) < 32 for ch in value):
        raise ValueError("Invalid SSH target")
    if any(token in value for token in ("/", "\\", ";", "&", "|", "$", "`", "<", ">")):
        raise ValueError("Invalid SSH target")

    if "@" in value:
        if value.count("@") != 1:
            raise ValueError("Invalid SSH target")
        user, host = value.split("@", 1)
        if not _USER_RE.fullmatch(user):
            raise ValueError("Invalid SSH user")
    else:
        host = value

    raw_host = host
    if host.startswith("[") and host.endswith("]"):
        raw_host = host[1:-1]
    try:
        ipaddress.ip_address(raw_host)
    except ValueError:
        if not _HOST_RE.fullmatch(raw_host):
            raise ValueError("Invalid SSH host")
    return value


def _validate_command(command: list[str]) -> list[str]:
    if not isinstance(command, list) or not command or len(command) > 128:
        raise ValueError("Remote command must be a nonempty argv list")
    result: list[str] = []
    for part in command:
        if not isinstance(part, str) or not part or len(part) > 4096 or "\x00" in part or "\n" in part or "\r" in part:
            raise ValueError("Invalid remote command argument")
        result.append(part)
    return result


def build_ssh_action(
    target: str,
    command: list[str],
    *,
    use_tailscale: bool = False,
    cwd: str | None = None,
) -> Action:
    valid_target = validate_ssh_target(target)
    argv = _validate_command(command)
    if use_tailscale:
        full_command = ["tailscale", "ssh", valid_target, *argv]
        transport = "tailscale-ssh"
    else:
        full_command = ["ssh", valid_target, *argv]
        transport = "ssh"
    return Action(
        "remote.ssh",
        full_command,
        cwd=cwd,
        metadata={
            "capability_id": "remote.ssh",
            "remote": True,
            "requires_approval": True,
            "transport": transport,
        },
    )


def _remote_action(args: dict[str, Any]) -> Action:
    context = args.get("__context__", {})
    return build_ssh_action(
        args["target"],
        args["command"],
        use_tailscale=bool(args.get("tailscale", False)),
        cwd=context.get("cwd"),
    )


def register_remote_capabilities(registry: CapabilityRegistry) -> CapabilityRegistry:
    if registry.get("remote.ssh") is None:
        registry.register(
            Capability(
                "remote.ssh",
                "Run an explicitly approved argv command over SSH or Tailscale SSH",
                _remote_action,
                (
                    ArgumentSpec("target"),
                    ArgumentSpec("command", kind="list[str]"),
                    ArgumentSpec("tailscale", kind="bool", required=False),
                ),
            )
        )
    return registry
