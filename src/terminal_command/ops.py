from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
import shutil
import tarfile
import zipfile
from collections import defaultdict
from pathlib import Path


def hash_file(path: str | Path, chunk_size: int = 1024 * 1024) -> str:
    target = Path(path)
    digest = hashlib.sha256()
    with target.open("rb") as handle:
        while chunk := handle.read(chunk_size):
            digest.update(chunk)
    return digest.hexdigest()


def search_files(root: str | Path, query: str, *, limit: int = 100) -> list[str]:
    base = Path(root).expanduser().resolve()
    safe_limit = max(1, min(int(limit), 10000))
    results: list[str] = []
    for path in base.rglob(query):
        if path.is_file():
            results.append(str(path.resolve()))
            if len(results) >= safe_limit:
                break
    return results


def duplicate_groups(root: str | Path, *, limit: int = 10000) -> list[dict]:
    base = Path(root).expanduser().resolve()
    safe_limit = max(1, min(int(limit), 100000))
    by_size: dict[int, list[Path]] = defaultdict(list)
    count = 0
    for path in base.rglob("*"):
        if not path.is_file():
            continue
        try:
            size = path.stat().st_size
        except OSError:
            continue
        by_size[size].append(path)
        count += 1
        if count >= safe_limit:
            break

    groups: list[dict] = []
    for size, candidates in by_size.items():
        if len(candidates) < 2:
            continue
        by_hash: dict[str, list[str]] = defaultdict(list)
        for path in candidates:
            try:
                by_hash[hash_file(path)].append(str(path.resolve()))
            except OSError:
                continue
        for digest, files in by_hash.items():
            if len(files) > 1:
                groups.append({"sha256": digest, "size": size, "files": sorted(files)})
    groups.sort(key=lambda item: (-item["size"], item["files"][0]))
    return groups


def list_archive(path: str | Path) -> list[str]:
    target = Path(path)
    if zipfile.is_zipfile(target):
        with zipfile.ZipFile(target) as handle:
            return handle.namelist()
    if tarfile.is_tarfile(target):
        with tarfile.open(target) as handle:
            return [member.name for member in handle.getmembers()]
    raise ValueError(f"Unsupported archive: {target}")


def create_archive(source: str | Path, output: str | Path) -> str:
    src = Path(source).expanduser().resolve()
    out = Path(output).expanduser().resolve()
    if out.suffix.lower() != ".zip":
        raise ValueError("Only .zip creation is supported")
    out.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(out, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        if src.is_file():
            archive.write(src, src.name)
        elif src.is_dir():
            for path in src.rglob("*"):
                if path.is_file() and path.resolve() != out:
                    archive.write(path, path.relative_to(src))
        else:
            raise ValueError(f"Source does not exist: {src}")
    return str(out)


def disk_info(path: str | Path) -> dict:
    target = Path(path).expanduser().resolve()
    usage = shutil.disk_usage(target)
    return {"path": str(target), "total": usage.total, "used": usage.used, "free": usage.free}


def system_info() -> dict:
    return {
        "platform": platform.platform(),
        "python": platform.python_version(),
        "machine": platform.machine(),
        "processor": platform.processor(),
        "cpu_count": os.cpu_count(),
    }


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="python -m terminal_command.ops")
    sub = parser.add_subparsers(dest="op", required=True)
    hash_p = sub.add_parser("hash")
    hash_p.add_argument("path")
    search_p = sub.add_parser("search")
    search_p.add_argument("root")
    search_p.add_argument("query")
    search_p.add_argument("--limit", type=int, default=100)
    dup_p = sub.add_parser("duplicates")
    dup_p.add_argument("root")
    dup_p.add_argument("--limit", type=int, default=10000)
    list_p = sub.add_parser("archive-list")
    list_p.add_argument("path")
    create_p = sub.add_parser("archive-create")
    create_p.add_argument("source")
    create_p.add_argument("output")
    disk_p = sub.add_parser("disk")
    disk_p.add_argument("path")
    sub.add_parser("system-info")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    if args.op == "hash":
        result = {"sha256": hash_file(args.path), "path": str(Path(args.path).resolve())}
    elif args.op == "search":
        result = search_files(args.root, args.query, limit=args.limit)
    elif args.op == "duplicates":
        result = duplicate_groups(args.root, limit=args.limit)
    elif args.op == "archive-list":
        result = list_archive(args.path)
    elif args.op == "archive-create":
        result = {"output": create_archive(args.source, args.output)}
    elif args.op == "disk":
        result = disk_info(args.path)
    else:
        result = system_info()
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
