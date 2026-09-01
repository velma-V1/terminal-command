from pathlib import Path

from terminal_command.checkpoints import CheckpointManager


def test_file_checkpoint_can_restore_explicit_files(tmp_path):
    state = tmp_path / "state"
    target = tmp_path / "config.txt"
    target.write_text("before", encoding="utf-8")
    manager = CheckpointManager(state)

    checkpoint = manager.create_files([target], label="before change")
    target.write_text("after", encoding="utf-8")
    restored = manager.restore_files(checkpoint.id)

    assert restored == [target.resolve()]
    assert target.read_text(encoding="utf-8") == "before"
    assert manager.get(checkpoint.id).label == "before change"


def test_git_checkpoint_records_head_without_mutating_repository(tmp_path):
    calls = []

    def runner(command):
        calls.append(command)
        return "abc123\n"

    repo = tmp_path / "repo"
    repo.mkdir()
    manager = CheckpointManager(tmp_path / "state", git_runner=runner)
    checkpoint = manager.create_git(repo, label="pre-fix")

    assert checkpoint.kind == "git"
    assert checkpoint.git_head == "abc123"
    assert calls == [["git", "-C", str(repo.resolve()), "rev-parse", "HEAD"]]


def test_checkpoint_list_is_newest_first(tmp_path):
    manager = CheckpointManager(tmp_path / "state")
    first_file = tmp_path / "a.txt"
    second_file = tmp_path / "b.txt"
    first_file.write_text("a", encoding="utf-8")
    second_file.write_text("b", encoding="utf-8")
    first = manager.create_files([first_file], label="first")
    second = manager.create_files([second_file], label="second")

    rows = manager.list()
    assert [row.id for row in rows][:2] == [second.id, first.id]
