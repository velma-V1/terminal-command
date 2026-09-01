from terminal_command.commands import CommandRegistry


def test_default_registry_contains_core_commands():
    registry = CommandRegistry.default()
    assert registry.names() == [
        "/benchmark",
        "/capabilities",
        "/checkpoint",
        "/doctor",
        "/exit",
        "/explain",
        "/help",
        "/history",
        "/jobs",
        "/project",
        "/update",
        "/workflow",
    ]
    assert registry.resolve("/doctor").name == "/doctor"


def test_completion_filters_prefix():
    registry = CommandRegistry.default()
    assert registry.completions("/h") == ["/help", "/history"]


def test_unknown_command_returns_none():
    assert CommandRegistry.default().resolve("/missing") is None
