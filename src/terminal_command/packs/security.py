from __future__ import annotations

import os
import shutil
import sys
from pathlib import Path

from ..capabilities import ArgumentSpec, Capability, CapabilityRegistry
from ..contracts import Action


def _root(args: dict) -> Path:
    context = args.get("__context__", {})
    value = args.get("root") or context.get("cwd") or str(Path.cwd())
    root = Path(value).expanduser().resolve()
    if not root.is_dir():
        raise ValueError(f"Directory does not exist: {root}")
    return root


def _meta(capability_id: str, *, tool: str, degraded: bool = False) -> dict:
    return {
        "capability_id": capability_id,
        "security": True,
        "requires_approval": True,
        "read_only": True,
        "tool": tool,
        "degraded": degraded,
    }


def _secret_action(args: dict) -> Action:
    root = _root(args)
    tool = shutil.which("gitleaks")
    if tool:
        command = [tool, "detect", "--source", str(root), "--no-banner", "--redact"]
        return Action("security.secrets", command, cwd=str(root), metadata=_meta("security.secrets", tool="gitleaks"))
    command = [sys.executable, "-m", "terminal_command.security_ops", "secrets", str(root)]
    return Action(
        "security.secrets",
        command,
        cwd=str(root),
        metadata=_meta("security.secrets", tool="internal-pattern-scan", degraded=True),
    )


def _dependency_action(args: dict) -> Action:
    root = _root(args)
    pip_audit = shutil.which("pip-audit")
    if pip_audit and ((root / "pyproject.toml").is_file() or (root / "requirements.txt").is_file()):
        command = [pip_audit]
        if (root / "requirements.txt").is_file():
            command += ["-r", "requirements.txt"]
        command += ["--format", "json"]
        return Action("security.deps_audit", command, cwd=str(root), metadata=_meta("security.deps_audit", tool="pip-audit"))

    npm = shutil.which("npm")
    if npm and (root / "package.json").is_file():
        return Action(
            "security.deps_audit",
            [npm, "audit", "--json"],
            cwd=str(root),
            metadata=_meta("security.deps_audit", tool="npm-audit"),
        )

    cargo_audit = shutil.which("cargo-audit")
    if cargo_audit and (root / "Cargo.toml").is_file():
        return Action(
            "security.deps_audit",
            [cargo_audit, "audit", "--json"],
            cwd=str(root),
            metadata=_meta("security.deps_audit", tool="cargo-audit"),
        )

    return Action(
        "security.deps_audit",
        [sys.executable, "-m", "terminal_command.security_ops", "deps-manifest", str(root)],
        cwd=str(root),
        metadata=_meta("security.deps_audit", tool="manifest-inventory", degraded=True),
    )


def _static_action(args: dict) -> Action:
    root = _root(args)
    config_value = args.get("config")
    semgrep = shutil.which("semgrep")
    if semgrep and config_value:
        config = Path(config_value).expanduser().resolve()
        if not config.is_file():
            raise ValueError(f"Semgrep config does not exist: {config}")
        command = [semgrep, "scan", "--config", str(config), "--metrics", "off", "--disable-version-check", str(root)]
        return Action("security.static_scan", command, cwd=str(root), metadata=_meta("security.static_scan", tool="semgrep"))

    return Action(
        "security.static_scan",
        [sys.executable, "-m", "terminal_command.security_ops", "static", str(root)],
        cwd=str(root),
        metadata=_meta("security.static_scan", tool="internal-static-patterns", degraded=True),
    )


def _network_action(args: dict) -> Action:
    if os.name == "nt":
        command = ["netstat", "-ano"]
        tool = "netstat"
    else:
        ss = shutil.which("ss")
        netstat = shutil.which("netstat")
        if ss:
            command = [ss, "-lntup"]
            tool = "ss"
        elif netstat:
            command = [netstat, "-an"]
            tool = "netstat"
        else:
            raise ValueError("No supported local network inspection tool found (ss/netstat)")
    return Action("security.network", command, metadata=_meta("security.network", tool=tool))


def register_security_pack(registry: CapabilityRegistry) -> CapabilityRegistry:
    root_arg = (ArgumentSpec("root", kind="path", required=False),)
    definitions = [
        Capability("security.secrets", "Scan an authorized local tree for likely secrets without printing secret values in the built-in fallback", _secret_action, root_arg),
        Capability("security.deps_audit", "Run an installed dependency vulnerability auditor or a degraded local manifest inventory", _dependency_action, root_arg),
        Capability(
            "security.static_scan",
            "Run Semgrep with an explicit local config when available, otherwise a bounded built-in static pattern scan",
            _static_action,
            (ArgumentSpec("root", kind="path", required=False), ArgumentSpec("config", kind="path", required=False)),
        ),
        Capability("security.network", "Inspect local listening/network state using installed OS tools", _network_action),
    ]
    for capability in definitions:
        if registry.get(capability.id) is None:
            registry.register(capability)
    return registry
