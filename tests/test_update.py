from __future__ import annotations

import hashlib
import io
import json
import os
from pathlib import Path
from types import SimpleNamespace

import pytest

from terminal_command.update import (
    UpdateManifest,
    apply_prepared_release,
    compare_versions,
    create_rollback_state,
    download_update_artifact,
    fetch_update_manifest,
    prepare_local_artifact,
    read_current_version,
    switch_current_version,
    verify_sha256,
)


class FakeResponse:
    def __init__(self, body: bytes, *, url: str):
        self._body = io.BytesIO(body)
        self._url = url
        self.status = 200
        self.headers = {"Content-Type": "application/json"}

    def read(self, size: int = -1) -> bytes:
        return self._body.read(size)

    def geturl(self) -> str:
        return self._url

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, tb):
        return False


class FakeOpener:
    def __init__(self, response: FakeResponse):
        self.response = response

    def open(self, request, timeout=None):
        return self.response


def test_update_manifest_requires_https_semver_and_sha256():
    manifest = UpdateManifest.from_dict(
        {
            "version": "0.2.0",
            "artifact_url": "https://example.com/terminal-command-0.2.0.whl",
            "sha256": "a" * 64,
        }
    )
    assert manifest.version == "0.2.0"

    invalid = [
        {"version": "bad", "artifact_url": "https://example.com/a.whl", "sha256": "a" * 64},
        {"version": "0.2.0", "artifact_url": "http://example.com/a.whl", "sha256": "a" * 64},
        {"version": "0.2.0", "artifact_url": "https://user:pass@example.com/a.whl", "sha256": "a" * 64},
        {"version": "0.2.0", "artifact_url": "https://example.com/a.whl", "sha256": "bad"},
    ]
    for payload in invalid:
        with pytest.raises(ValueError):
            UpdateManifest.from_dict(payload)


def test_fetch_update_manifest_is_bounded_https_and_schema_validated():
    body = json.dumps(
        {
            "version": "0.2.0",
            "artifact_url": "https://example.com/terminal-command-0.2.0.whl",
            "sha256": "a" * 64,
        }
    ).encode("utf-8")
    opener = FakeOpener(FakeResponse(body, url="https://example.com/update-manifest.json"))
    manifest = fetch_update_manifest("https://example.com/update-manifest.json", opener=opener)
    assert manifest.version == "0.2.0"


def test_version_comparison_handles_release_and_prerelease():
    assert compare_versions("0.2.0", "0.1.9") > 0
    assert compare_versions("0.2.0", "0.2.0") == 0
    assert compare_versions("0.2.0-rc.1", "0.2.0") < 0
    assert compare_versions("1.0.0", "1.0.0-rc.9") > 0


def test_hash_verification_uses_sha256(tmp_path):
    artifact = tmp_path / "artifact.whl"
    artifact.write_bytes(b"wheel-bytes")
    digest = hashlib.sha256(b"wheel-bytes").hexdigest()
    assert verify_sha256(artifact, digest) is True
    assert verify_sha256(artifact, "0" * 64) is False


def test_prepare_local_artifact_verifies_and_stages_without_switching_current(tmp_path):
    install_root = tmp_path / "install"
    artifact = tmp_path / "terminal_command-0.2.0.whl"
    artifact.write_bytes(b"wheel")
    digest = hashlib.sha256(b"wheel").hexdigest()
    manifest = UpdateManifest("0.2.0", "https://example.com/a.whl", digest)
    (install_root / "releases" / "0.1.0").mkdir(parents=True)
    switch_current_version(install_root, "0.1.0")

    staged = prepare_local_artifact(manifest, artifact, install_root, current_version="0.1.0")

    assert staged.exists()
    assert staged.read_bytes() == b"wheel"
    assert read_current_version(install_root) == "0.1.0"


def test_download_update_artifact_hash_verifies_before_staging(tmp_path):
    install_root = tmp_path / "install"
    (install_root / "releases" / "0.1.0").mkdir(parents=True)
    switch_current_version(install_root, "0.1.0")
    body = b"downloaded-wheel"
    manifest = UpdateManifest(
        "0.2.0",
        "https://example.com/terminal-command-0.2.0.whl",
        hashlib.sha256(body).hexdigest(),
    )
    opener = FakeOpener(FakeResponse(body, url=manifest.artifact_url))

    staged = download_update_artifact(manifest, install_root, current_version="0.1.0", opener=opener)

    assert staged.read_bytes() == body
    assert read_current_version(install_root) == "0.1.0"


def test_download_rejects_hash_mismatch_and_does_not_stage(tmp_path):
    manifest = UpdateManifest("0.2.0", "https://example.com/a.whl", "0" * 64)
    opener = FakeOpener(FakeResponse(b"not-the-wheel", url=manifest.artifact_url))
    with pytest.raises(ValueError, match="sha256"):
        download_update_artifact(manifest, tmp_path / "install", current_version="0.1.0", opener=opener)


def test_prepare_rejects_same_or_older_version(tmp_path):
    artifact = tmp_path / "a.whl"
    artifact.write_bytes(b"x")
    digest = hashlib.sha256(b"x").hexdigest()
    manifest = UpdateManifest("0.1.0", "https://example.com/a.whl", digest)
    with pytest.raises(ValueError, match="newer"):
        prepare_local_artifact(manifest, artifact, tmp_path / "install", current_version="0.1.0")


def test_rollback_state_and_current_pointer_are_atomic_release_metadata(tmp_path):
    install_root = tmp_path / "install"
    (install_root / "releases" / "0.1.0").mkdir(parents=True)
    (install_root / "releases" / "0.2.0").mkdir(parents=True)
    switch_current_version(install_root, "0.1.0")

    state = create_rollback_state(install_root, current_version="0.1.0", target_version="0.2.0")
    payload = json.loads(state.read_text(encoding="utf-8"))
    assert payload["previous_version"] == "0.1.0"
    assert payload["target_version"] == "0.2.0"

    switch_current_version(install_root, "0.2.0")
    assert read_current_version(install_root) == "0.2.0"
    switch_current_version(install_root, payload["previous_version"])
    assert read_current_version(install_root) == "0.1.0"


def test_current_pointer_rejects_release_that_does_not_exist(tmp_path):
    with pytest.raises(ValueError, match="release"):
        switch_current_version(tmp_path / "install", "9.9.9")


def test_apply_prepared_release_switches_pointer_only_after_doctor_passes(tmp_path):
    install_root = tmp_path / "install"
    (install_root / "releases" / "0.1.0").mkdir(parents=True)
    switch_current_version(install_root, "0.1.0")
    staged_dir = install_root / "staging" / "0.2.0"
    staged_dir.mkdir(parents=True)
    artifact = staged_dir / "terminal-command.whl"
    artifact.write_bytes(b"wheel")

    commands = []

    def fake_venv_builder(release_dir: Path):
        scripts = release_dir / ("Scripts" if os.name == "nt" else "bin")
        scripts.mkdir(parents=True)
        (scripts / ("python.exe" if os.name == "nt" else "python")).write_text("", encoding="utf-8")
        (scripts / ("terminal-command.exe" if os.name == "nt" else "terminal-command")).write_text("", encoding="utf-8")

    def fake_runner(command, **kwargs):
        commands.append(list(command))
        return SimpleNamespace(returncode=0, stdout="core: ok", stderr="")

    result = apply_prepared_release(
        install_root,
        "0.2.0",
        artifact,
        venv_builder=fake_venv_builder,
        runner=fake_runner,
    )

    assert result == "0.2.0"
    assert read_current_version(install_root) == "0.2.0"
    assert any("pip" in part for command in commands for part in command)
    assert any("--doctor" in command for command in commands)


def test_apply_failure_leaves_current_release_unchanged(tmp_path):
    install_root = tmp_path / "install"
    (install_root / "releases" / "0.1.0").mkdir(parents=True)
    switch_current_version(install_root, "0.1.0")
    staged_dir = install_root / "staging" / "0.2.0"
    staged_dir.mkdir(parents=True)
    artifact = staged_dir / "terminal-command.whl"
    artifact.write_bytes(b"wheel")

    def fake_venv_builder(release_dir: Path):
        scripts = release_dir / ("Scripts" if os.name == "nt" else "bin")
        scripts.mkdir(parents=True)
        (scripts / ("python.exe" if os.name == "nt" else "python")).write_text("", encoding="utf-8")
        (scripts / ("terminal-command.exe" if os.name == "nt" else "terminal-command")).write_text("", encoding="utf-8")

    calls = 0

    def fake_runner(command, **kwargs):
        nonlocal calls
        calls += 1
        if calls == 1:
            return SimpleNamespace(returncode=0, stdout="", stderr="")
        return SimpleNamespace(returncode=1, stdout="", stderr="doctor failed")

    with pytest.raises(RuntimeError, match="doctor"):
        apply_prepared_release(
            install_root,
            "0.2.0",
            artifact,
            venv_builder=fake_venv_builder,
            runner=fake_runner,
        )
    assert read_current_version(install_root) == "0.1.0"
