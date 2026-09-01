from pathlib import Path

from terminal_command.projects import ProjectStore


def test_project_store_register_resume_notes_and_state(tmp_path):
    root = tmp_path / "repo"
    root.mkdir()
    store = ProjectStore(tmp_path / "projects.json")

    project = store.register(root, name="demo")
    assert project.name == "demo"
    assert Path(project.root) == root.resolve()

    store.set_current("demo")
    store.add_note("demo", "remember this")
    store.update_state("demo", "last_task", "tests")

    current = store.current()
    assert current is not None
    assert current.notes == ["remember this"]
    assert current.state["last_task"] == "tests"

    reloaded = ProjectStore(tmp_path / "projects.json")
    assert reloaded.current().name == "demo"
    assert reloaded.get("demo").notes == ["remember this"]


def test_project_discovery_finds_nearest_git_root_and_can_register(tmp_path):
    root = tmp_path / "repo"
    nested = root / "a" / "b"
    nested.mkdir(parents=True)
    (root / ".git").mkdir()
    store = ProjectStore(tmp_path / "projects.json")

    discovered = store.discover(nested, register=True)
    assert discovered is not None
    assert Path(discovered.root) == root.resolve()
    assert store.get(discovered.name) is not None


def test_project_store_rejects_duplicate_name_for_different_root(tmp_path):
    first = tmp_path / "first"
    second = tmp_path / "second"
    first.mkdir()
    second.mkdir()
    store = ProjectStore(tmp_path / "projects.json")
    store.register(first, name="same")

    try:
        store.register(second, name="same")
    except ValueError as exc:
        assert "same" in str(exc)
    else:
        raise AssertionError("duplicate name should fail")
