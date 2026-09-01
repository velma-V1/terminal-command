from __future__ import annotations

import argparse
import json
import sys
from dataclasses import dataclass
from typing import Mapping
from urllib.parse import urlsplit
from urllib.request import Request, build_opener

from .capabilities import ArgumentSpec, Capability, CapabilityRegistry
from .contracts import Action

_MAX_RESPONSE_BYTES = 10 * 1024 * 1024
_MAX_TIMEOUT_SECONDS = 30.0


@dataclass(frozen=True, slots=True)
class WebResponse:
    url: str
    status: int | None
    headers: dict[str, str]
    body: bytes


def validate_url(url: str) -> str:
    value = str(url).strip()
    if not value or any(ord(ch) < 32 or ch.isspace() for ch in value):
        raise ValueError("Invalid URL")
    parsed = urlsplit(value)
    if parsed.scheme.lower() not in {"http", "https"}:
        raise ValueError("Only http/https URLs are allowed")
    if not parsed.hostname:
        raise ValueError("URL must include a host")
    if parsed.username is not None or parsed.password is not None:
        raise ValueError("Embedded URL credentials are not allowed")
    return value


def _limits(max_bytes: int, timeout: float) -> tuple[int, float]:
    size = int(max_bytes)
    seconds = float(timeout)
    if size < 1 or size > _MAX_RESPONSE_BYTES:
        raise ValueError(f"max_bytes must be between 1 and {_MAX_RESPONSE_BYTES}")
    if seconds <= 0 or seconds > _MAX_TIMEOUT_SECONDS:
        raise ValueError(f"timeout must be greater than 0 and at most {_MAX_TIMEOUT_SECONDS}")
    return size, seconds


def fetch_url(
    url: str,
    *,
    max_bytes: int = 1_000_000,
    timeout: float = 10.0,
    opener=None,
) -> WebResponse:
    requested = validate_url(url)
    size, seconds = _limits(max_bytes, timeout)
    client = opener or build_opener()
    request = Request(requested, headers={"User-Agent": "terminal-command/0.1"}, method="GET")
    with client.open(request, timeout=seconds) as response:
        final_url = validate_url(response.geturl())
        body = response.read(size + 1)
        if len(body) > size:
            raise ValueError(f"HTTP response exceeds max_bytes={size}")
        headers_obj = getattr(response, "headers", {})
        headers: dict[str, str]
        if isinstance(headers_obj, Mapping):
            headers = {str(key): str(value) for key, value in headers_obj.items()}
        else:
            headers = {}
        status = getattr(response, "status", None)
        return WebResponse(final_url, int(status) if status is not None else None, headers, body)


def build_fetch_action(url: str, *, max_bytes: int = 1_000_000, timeout: float = 10.0, cwd: str | None = None) -> Action:
    valid = validate_url(url)
    size, seconds = _limits(max_bytes, timeout)
    return Action(
        "web.fetch",
        [
            sys.executable,
            "-m",
            "terminal_command.web_adapter",
            valid,
            "--max-bytes",
            str(size),
            "--timeout",
            str(seconds),
        ],
        cwd=cwd,
        metadata={
            "capability_id": "web.fetch",
            "network": True,
            "remote": True,
            "requires_approval": True,
            "read_only": True,
        },
    )


def _capability_action(args: dict) -> Action:
    context = args.get("__context__", {})
    return build_fetch_action(
        args["url"],
        max_bytes=int(args.get("max_bytes", 1_000_000)),
        timeout=float(args.get("timeout", 10.0)),
        cwd=context.get("cwd"),
    )


def register_web_capability(registry: CapabilityRegistry) -> CapabilityRegistry:
    if registry.get("web.fetch") is None:
        registry.register(
            Capability(
                "web.fetch",
                "Fetch a bounded http/https response after explicit approval",
                _capability_action,
                (
                    ArgumentSpec("url"),
                    ArgumentSpec("max_bytes", kind="int", required=False),
                    ArgumentSpec("timeout", kind="float", required=False),
                ),
            )
        )
    return registry


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="python -m terminal_command.web_adapter")
    parser.add_argument("url")
    parser.add_argument("--max-bytes", type=int, default=1_000_000)
    parser.add_argument("--timeout", type=float, default=10.0)
    return parser


def main(argv: list[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    response = fetch_url(args.url, max_bytes=args.max_bytes, timeout=args.timeout)
    content_type = response.headers.get("Content-Type", "")
    payload = {
        "url": response.url,
        "status": response.status,
        "content_type": content_type,
        "body": response.body.decode("utf-8", errors="replace"),
    }
    print(json.dumps(payload, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
