from __future__ import annotations

import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import uuid
import venv
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Callable
from urllib.parse import urlsplit

from .web_adapter import fetch_url, validate_url

_SEMVER_RE = re.compile(
    r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z.-]+))?$"
)
_SHA256_RE = re.compile(r"^[0-9a-fA-F]{64}$")


@dataclass(frozen=True, slots=True)
class UpdateManifest:
    version: str
    artifact_url: str
    sha256: str

    def __post_init__(self) -> None:
        _parse_semver(self.version)
        parsed = urlsplit(validate_url(self.artifact_url))
        if parsed.scheme.lower() != "https":
            raise ValueError("Update artifacts must use https")
        if not _SHA256_RE.fullmatch(self.sha256):
            raise ValueError("Update sha256 must be 64 hexadecimal characters")

    @classmethod
    def from_dict(cls, payload: dict) -> "UpdateManifest":
        if not isinstance(payload, dict):
            raise ValueError("Update manifest must be an object")
        return cls(
            str(payload.get("version", "")),
            str(payload.get("artifact_url", "")),
            str(payload.get("sha256", "")),
        )


def _parse_semver(value: str) -> tuple[int, int, int, tuple[str, ...] | None]:
    match = _SEMVER_RE.fullmatch(value)
    if not match:
        raise ValueError(f"Invalid semantic version: {value}")
    prerelease = tuple(match.group(4).split(".")) if match.group(4) else None
    if prerelease is not None and any(not part for part in prerelease):
        raise ValueError(f"Invalid semantic version: {value}")
    return int(match.group(1)), int(match.group(2)), int(match.group(3)), prerelease


def compare_versions(left: str, right: str) -> int:
    l_major, l_minor, l_patch, l_pre = _parse_semver(left)
    r_major, r_minor, r_patch, r_pre = _parse_semver(right)
    core_left = (l_major, l_minor, l_patch)
    core_right = (r_major, r_minor, r_patch)
    if core_left != core_right:
        return 1 if core_left > core_right else -1
    if l_pre is None and r_pre is None:
        return 0
    if l_pre is None:
        return 1
    if r_pre is None:
        return -1
    for l_part, r_part in zip(l_pre, r_pre):
        if l_part == r_part:
            continue
        l_num = l_part.isdigit()
        r_num = r_part.isdigit()
        if l_num and r_num:
            return 1 if int(l_part) > int(r_part) else -1
        if l_num != r_num:
            return -1 if l_num else 1
        return 1 if l_part > r_part else -1
    if len(l_pre) == len(r_pre):
        return 0
    return 1 if len(l_pre) > len(r_pre) else -1


def verify_sha256(path: str | Path, expected: str) -> bool:
    if not _SHA256_RE.fullmatch(expected):
        raise ValueError("Expected sha256 must be 64 hexadecimal characters")
    digest = hashlib.sha256()
    with Path(path).open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().lower() == expected.lower()


def fetch_update_manifest(url: str, *, opener=None) -> UpdateManifest:
    parsed = urlsplit(validate_url(url))
    if parsed.scheme.lower() != "https":
        raise ValueError("Update manifests must use https")
    response = fetch_url(url, max_bytes=256 * 1024, timeout=10.0, opener=opener)
    try:
        payload = json.loads(response.body.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ValueError("Invalid update manifest JSON") from exc
    return UpdateManifest.from_dict(payload)


def read_current_version(install_root: str | Path) -> str | None:
    path = Path(install_root).expanduser().resolve() / "current.txt"
    if not path.is_file():
        return None
    value = path.read_text(encoding="utf-8").strip()
    if not value:
        return None
    _parse_semver(value)
    return value


def switch_current_version(install_root: str | Path, version: str) -> Path:
    _parse_semver(version)
    root = Path(install_root).expanduser().resolve()
    release = root / "releases" / version
    if not release.is_dir():
        raise ValueError(f"Release does not exist: {release}")
    root.mkdir(parents=True, exist_ok=True)
    current = root / "current.txt"
    temp = root / f"current.{uuid.uuid4().hex}.tmp"
    temp.write_text(version + "\n", encoding="utf-8")
    os.replace(temp, current)
    return current


def create_rollback_state(
    install_root: str | Path,
    *,
    current_version: str,
    target_version: str,
) -> Path:
    _parse_semver(current_version)
    _parse_semver(target_version)
    root = Path(install_root).expanduser().resolve()
    root.mkdir(parents=True, exist_ok=True)
    payload = {
        "version": 1,
        "previous_version": current_version,
        "target_version": target_version,
    }
    path = root / "rollback.json"
    temp = root / f"rollback.{uuid.uuid4().hex}.tmp"
    temp.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    os.replace(temp, path)
    return path


def _validate_newer(manifest: UpdateManifest, current_version: str) -> None:
    if compare_versions(manifest.version, current_version) <= 0:
        raise ValueError(
            f"Update version {manifest.version} is not newer than current version {current_version}"
        )


def prepare_local_artifact(
    manifest: UpdateManifest,
    artifact: str | Path,
    install_root: str | Path,
    *,
    current_version: str,
) -> Path:
    _validate_newer(manifest, current_version)
    source = Path(artifact).expanduser().resolve()
    if not source.is_file():
        raise ValueError(f"Update artifact does not exist: {source}")
    if not verify_sha256(source, manifest.sha256):
        raise ValueError("Update artifact sha256 mismatch")
    root = Path(install_root).expanduser().resolve()
    staging = root / "staging" / manifest.version
    staging.mkdir(parents=True, exist_ok=True)
    suffix = source.suffix if source.suffix else ".whl"
    destination = staging / f"terminal-command-{manifest.version}{suffix}"
    temp = staging / f"artifact.{uuid.uuid4().hex}.tmp"
    shutil.copy2(source, temp)
    if not verify_sha256(temp, manifest.sha256):
        temp.unlink(missing_ok=True)
        raise ValueError("Staged update artifact sha256 mismatch")
    os.replace(temp, destination)
    metadata = staging / "manifest.json"
    metadata.write_text(json.dumps(asdict(manifest), indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return destination


def download_update_artifact(
    manifest: UpdateManifest,
    install_root: str | Path,
    *,
    current_version: str,
    opener=None,
) -> Path:
    _validate_newer(manifest, current_version)
    response = fetch_url(
        manifest.artifact_url,
        max_bytes=10 * 1024 * 1024,
        timeout=30.0,
        opener=opener,
    )
    root = Path(install_root).expanduser().resolve()
    incoming = root / "incoming"
    incoming.mkdir(parents=True, exist_ok=True)
    temp = incoming / f"{manifest.version}.{uuid.uuid4().hex}.download"
    temp.write_bytes(response.body)
    try:
        if not verify_sha256(temp, manifest.sha256):
            raise ValueError("Update artifact sha256 mismatch")
        return prepare_local_artifact(manifest, temp, root, current_version=current_version)
    finally:
        temp.unlink(missing_ok=True)


def _verify_staged_manifest(source: Path, target_version: str) -> None:
    manifest_path = source.parent / "manifest.json"
    if not manifest_path.is_file():
        return
    try:
        payload = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ValueError("Prepared update manifest is invalid") from exc
    manifest = UpdateManifest.from_dict(payload)
    if manifest.version != target_version:
        raise ValueError("Prepared update manifest version does not match target release")
    if not verify_sha256(source, manifest.sha256):
        raise ValueError("Prepared update artifact sha256 mismatch")


def _default_venv_builder(path: Path) -> None:
    venv.EnvBuilder(with_pip=True, clear=False).create(path)


def _release_executables(release_dir: Path) -> tuple[Path, Path]:
    scripts = release_dir / ("Scripts" if os.name == "nt" else "bin")
    python_exe = scripts / ("python.exe" if os.name == "nt" else "python")
    app_exe = scripts / ("terminal-command.exe" if os.name == "nt" else "terminal-command")
    return python_exe, app_exe


def apply_prepared_release(
    install_root: str | Path,
    target_version: str,
    artifact: str | Path,
    *,
    venv_builder: Callable[[Path], None] | None = None,
    runner: Callable[..., object] | None = None,
) -> str:
    _parse_semver(target_version)
    root = Path(install_root).expanduser().resolve()
    source = Path(artifact).expanduser().resolve()
    if not source.is_file():
        raise ValueError(f"Prepared artifact does not exist: {source}")
    _verify_staged_manifest(source, target_version)
    current_version = read_current_version(root)
    if current_version is None:
        raise ValueError("No current installed release exists")
    if compare_versions(target_version, current_version) <= 0:
        raise ValueError("Target release must be newer than current release")
    final_release = root / "releases" / target_version
    if final_release.exists():
        raise ValueError(f"Target release already exists: {final_release}")
    final_release.parent.mkdir(parents=True, exist_ok=True)
    builder = venv_builder or _default_venv_builder
    command_runner = runner or subprocess.run
    activated = False
    try:
        builder(final_release)
        python_exe, app_exe = _release_executables(final_release)
        install = command_runner(
            [str(python_exe), "-m", "pip", "install", "--upgrade", str(source)],
            capture_output=True,
            text=True,
        )
        if getattr(install, "returncode", 1) != 0:
            detail = getattr(install, "stderr", "") or getattr(install, "stdout", "")
            raise RuntimeError(f"Update install failed: {detail}".strip())
        doctor = command_runner(
            [str(app_exe), "--doctor"],
            capture_output=True,
            text=True,
        )
        if getattr(doctor, "returncode", 1) != 0:
            detail = getattr(doctor, "stderr", "") or getattr(doctor, "stdout", "")
            raise RuntimeError(f"Update doctor validation failed: {detail}".strip())
        create_rollback_state(root, current_version=current_version, target_version=target_version)
        switch_current_version(root, target_version)
        activated = True
        return target_version
    finally:
        if not activated and final_release.exists():
            shutil.rmtree(final_release, ignore_errors=True)
