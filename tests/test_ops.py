from __future__ import annotations

import hashlib
import zipfile
from pathlib import Path

from terminal_command.ops import duplicate_groups, hash_file, list_archive, search_files


def test_hash_file_uses_sha256(tmp_path):
    target = tmp_path / "x.txt"
    target.write_bytes(b"abc")
    assert hash_file(target) == hashlib.sha256(b"abc").hexdigest()


def test_duplicate_groups_hashes_only_equal_sized_candidates(tmp_path):
    (tmp_path / "a.txt").write_text("same", encoding="utf-8")
    (tmp_path / "b.txt").write_text("same", encoding="utf-8")
    (tmp_path / "c.txt").write_text("other", encoding="utf-8")
    groups = duplicate_groups(tmp_path, limit=100)
    assert len(groups) == 1
    assert {Path(item).name for item in groups[0]["files"]} == {"a.txt", "b.txt"}


def test_search_files_is_bounded_and_pattern_based(tmp_path):
    for name in ("a.py", "b.py", "c.txt"):
        (tmp_path / name).write_text(name, encoding="utf-8")
    rows = search_files(tmp_path, "*.py", limit=1)
    assert len(rows) == 1
    assert rows[0].endswith(".py")


def test_list_archive_reads_zip_members_without_extracting(tmp_path):
    archive = tmp_path / "demo.zip"
    with zipfile.ZipFile(archive, "w") as handle:
        handle.writestr("a.txt", "hello")
    assert list_archive(archive) == ["a.txt"]
