from __future__ import annotations

import os
import platform
import sys
from pathlib import Path
from urllib.parse import urlparse

from ..capabilities import ArgumentSpec, Capability, CapabilityRegistry
from ..contracts import Action


def _context_cwd(args: dict) -> str | None:
    value = args.get("__context__", {}).get("cwd")
    return str(value) if value else None


def _internal(op: str, *parts: str, read_only: bool = True, capability_id: str, args: dict | None = None) -> Action:
    return Action(
        capability_id,
        [sys.executable, "-m", "terminal_command.ops", op, *parts],
        cwd=_context_cwd(args or {}),
        metadata={"capability_id": capability_id, "read_only": read_only},
    )


def _existing_dir(raw: str) -> Path:
    path = Path(raw).expanduser().resolve()
    if not path.exists() or not path.is_dir():
        raise ValueError(f"Directory does not exist: {path}")
    return path


def _existing_path(raw: str) -> Path:
    path = Path(raw).expanduser().resolve()
    if not path.exists():
        raise ValueError(f"Path does not exist: {path}")
    return path


def _search(args: dict) -> Action:
    root = _existing_dir(args["root"])
    limit = int(args.get("limit", 100))
    if limit < 1 or limit > 10000:
        raise ValueError("limit must be between 1 and 10000")
    return _internal("search", str(root), args["query"], "--limit", str(limit), capability_id="files.search", args=args)


def _hash(args: dict) -> Action:
    path = _existing_path(args["path"])
    if not path.is_file():
        raise ValueError("Hash target must be a file")
    return _internal("hash", str(path), capability_id="files.hash", args=args)


def _duplicates(args: dict) -> Action:
    root = _existing_dir(args["root"])
    limit = int(args.get("limit", 10000))
    if limit < 1 or limit > 100000:
        raise ValueError("limit must be between 1 and 100000")
    return _internal("duplicates", str(root), "--limit", str(limit), capability_id="files.duplicates", args=args)


def _archive_list(args: dict) -> Action:
    return _internal("archive-list", str(Path(args["path"]).expanduser().resolve()), capability_id="archive.list", args=args)


def _archive_create(args: dict) -> Action:
    source = _existing_path(args["source"])
    output = Path(args["output"]).expanduser().resolve()
    return _internal(
        "archive-create",
        str(source),
        str(output),
        read_only=False,
        capability_id="archive.create",
        args=args,
    )


def _disk(args: dict) -> Action:
    context = args.get("__context__", {})
    path = _existing_path(args.get("path") or context.get("cwd") or str(Path.cwd()))
    return _internal("disk", str(path), capability_id="system.disk", args=args)


def _launch_command(target: str) -> list[str]:
    if os.name == "nt":
        return ["cmd", "/c", "start", "", target]
    if platform.system() == "Darwin":
        return ["open", target]
    return ["xdg-open", target]


def _launch_url(args: dict) -> Action:
    url = args["url"].strip()
    parsed = urlparse(url)
    if parsed.scheme not in {"http", "https"} or not parsed.netloc:
        raise ValueError("Only http/https URLs are allowed")
    return Action(
        "launch.url",
        _launch_command(url),
        cwd=_context_cwd(args),
        metadata={"capability_id": "launch.url", "launch": True},
    )


def _launch_path(args: dict) -> Action:
    path = _existing_path(args["path"])
    return Action(
        "launch.path",
        _launch_command(str(path)),
        cwd=_context_cwd(args),
        metadata={"capability_id": "launch.path", "launch": True},
    )


def register_daily_pack(registry: CapabilityRegistry) -> CapabilityRegistry:
    definitions = [
        Capability(
            "files.search",
            "Search local files by glob pattern",
            _search,
            (
                ArgumentSpec("root", kind="path"),
                ArgumentSpec("query"),
                ArgumentSpec("limit", kind="int", required=False),
            ),
        ),
        Capability("files.hash", "Calculate SHA-256 for a local file", _hash, (ArgumentSpec("path", kind="path"),)),
        Capability(
            "files.duplicates",
            "Find duplicate local files by size and SHA-256",
            _duplicates,
            (ArgumentSpec("root", kind="path"), ArgumentSpec("limit", kind="int", required=False)),
        ),
        Capability("archive.list", "List archive contents without extraction", _archive_list, (ArgumentSpec("path", kind="path"),)),
        Capability(
            "archive.create",
            "Create a ZIP archive from a local path",
            _archive_create,
            (ArgumentSpec("source", kind="path"), ArgumentSpec("output", kind="path")),
        ),
        Capability("system.disk", "Inspect disk usage", _disk, (ArgumentSpec("path", kind="path", required=False),)),
        Capability(
            "system.info",
            "Inspect local platform information",
            lambda args: _internal("system-info", capability_id="system.info", args=args),
        ),
        Capability("launch.url", "Open an http/https URL in the default application", _launch_url, (ArgumentSpec("url"),)),
        Capability("launch.path", "Open a local path in its default application", _launch_path, (ArgumentSpec("path", kind="path"),)),
    ]
    for capability in definitions:
        if registry.get(capability.id) is None:
            registry.register(capability)
    return registry
