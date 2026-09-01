from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

_SKIP_DIRS = {".git", ".venv", "venv", "node_modules", "dist", "build", "target", "__pycache__"}
_MAX_FILE_BYTES = 2 * 1024 * 1024

_SECRET_RULES = (
    ("aws-access-key-id", re.compile(r"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b")),
    ("openai-style-key", re.compile(r"\bsk-[A-Za-z0-9_-]{20,}\b")),
    ("private-key", re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----")),
    ("credential-assignment", re.compile(r"(?i)\b(?:password|passwd|secret|api[_-]?key|token)\b\s*[:=]\s*['\"][^'\"]{8,}['\"]")),
)

_STATIC_RULES = (
    ("python-eval", re.compile(r"\beval\s*\(")),
    ("python-exec", re.compile(r"\bexec\s*\(")),
    ("subprocess-shell", re.compile(r"subprocess\.[A-Za-z_]+\([^\n]*shell\s*=\s*True")),
    ("pickle-loads", re.compile(r"\bpickle\.loads?\s*\(")),
)


def _iter_text_files(root: Path, *, max_files: int = 10000):
    count = 0
    for path in root.rglob("*"):
        if any(part in _SKIP_DIRS for part in path.parts):
            continue
        if not path.is_file():
            continue
        try:
            if path.stat().st_size > _MAX_FILE_BYTES:
                continue
            raw = path.read_bytes()
        except OSError:
            continue
        if b"\x00" in raw[:4096]:
            continue
        try:
            text = raw.decode("utf-8")
        except UnicodeDecodeError:
            continue
        yield path, text
        count += 1
        if count >= max_files:
            break


def _scan(root: str | Path, rules, *, max_files: int = 10000) -> list[dict]:
    base = Path(root).expanduser().resolve()
    if not base.is_dir():
        raise ValueError(f"Directory does not exist: {base}")
    findings: list[dict] = []
    for path, text in _iter_text_files(base, max_files=max_files):
        for line_number, line in enumerate(text.splitlines(), start=1):
            for rule_id, pattern in rules:
                if pattern.search(line):
                    findings.append({"path": str(path), "line": line_number, "rule": rule_id})
    return findings


def scan_secrets(root: str | Path, *, max_files: int = 10000) -> list[dict]:
    return _scan(root, _SECRET_RULES, max_files=max_files)


def static_scan(root: str | Path, *, max_files: int = 10000) -> list[dict]:
    return _scan(root, _STATIC_RULES, max_files=max_files)


def dependency_manifest(root: str | Path) -> dict:
    base = Path(root).expanduser().resolve()
    if not base.is_dir():
        raise ValueError(f"Directory does not exist: {base}")
    candidates = ("pyproject.toml", "requirements.txt", "package.json", "package-lock.json", "Cargo.toml", "Cargo.lock", "go.mod", "go.sum")
    manifests = [str((base / name).resolve()) for name in candidates if (base / name).is_file()]
    return {"root": str(base), "manifests": manifests, "degraded": True, "note": "Inventory only; no vulnerability database was queried."}


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="python -m terminal_command.security_ops")
    sub = parser.add_subparsers(dest="op", required=True)
    for name in ("secrets", "static", "deps-manifest"):
        child = sub.add_parser(name)
        child.add_argument("root")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    if args.op == "secrets":
        result = scan_secrets(args.root)
    elif args.op == "static":
        result = static_scan(args.root)
    else:
        result = dependency_manifest(args.root)
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
