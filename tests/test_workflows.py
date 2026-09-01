from terminal_command.capabilities import Capability, CapabilityRegistry
from terminal_command.contracts import Action, ExecutionResult
from terminal_command.execution import Executor
from terminal_command.policy import PolicyEngine
from terminal_command.workflows import Workflow, WorkflowRunner, WorkflowStep, WorkflowStore


class FakeExecutor:
    def __init__(self, fail_on=None):
        self.fail_on = fail_on
        self.commands = []

    def execute(self, action):
        self.commands.append(action.command)
        failed = self.fail_on is not None and action.command[-1] == self.fail_on
        return ExecutionResult(
            backend="native",
            exit_code=1 if failed else 0,
            stdout="" if failed else "ok",
            stderr="boom" if failed else "",
            duration_ms=1.0,
            status="failed" if failed else "success",
        )


def _registry():
    registry = CapabilityRegistry()
    registry.register(
        Capability(
            id="demo.echo",
            description="Echo",
            builder=lambda args: Action("demo.echo", ["echo", args["text"]], metadata={"capability_id": "demo.echo"}),
            arguments=(),
        )
    )
    # Builder above accepts free values only when workflow directly supplies none; use fixed capabilities for runner tests.
    registry.register(Capability(id="demo.one", description="One", builder=lambda args: Action("demo.one", ["echo", "one"])))
    registry.register(Capability(id="demo.fail", description="Fail", builder=lambda args: Action("demo.fail", ["echo", "fail"])))
    registry.register(Capability(id="demo.after", description="After", builder=lambda args: Action("demo.after", ["echo", "after"])))
    return registry


def test_workflow_store_round_trips_versioned_json(tmp_path):
    store = WorkflowStore(tmp_path / "workflows.json")
    workflow = Workflow(
        name="check",
        description="demo",
        steps=(WorkflowStep("demo.one"), WorkflowStep("demo.after", required=False)),
    )
    store.save(workflow)

    loaded = WorkflowStore(tmp_path / "workflows.json").get("check")
    assert loaded == workflow
    assert [item.name for item in store.list()] == ["check"]


def test_workflow_runner_stops_after_failed_required_step():
    registry = _registry()
    executor = FakeExecutor(fail_on="fail")
    runner = WorkflowRunner(registry, PolicyEngine(), executor)
    workflow = Workflow(
        name="stop",
        steps=(WorkflowStep("demo.one"), WorkflowStep("demo.fail"), WorkflowStep("demo.after")),
    )

    result = runner.run(workflow, approved=True)
    assert result.status == "failed"
    assert executor.commands == [["echo", "one"], ["echo", "fail"]]
    assert len(result.steps) == 2


def test_workflow_runner_never_bypasses_required_approval():
    registry = _registry()
    executor = FakeExecutor()
    runner = WorkflowRunner(registry, PolicyEngine(), executor)
    workflow = Workflow(name="approval", steps=(WorkflowStep("demo.one"),))

    result = runner.run(workflow, approved=False)
    assert result.status == "approval_required"
    assert executor.commands == []


def test_workflow_rejects_unknown_capability_before_execution():
    runner = WorkflowRunner(_registry(), PolicyEngine(), FakeExecutor())
    result = runner.run(Workflow(name="bad", steps=(WorkflowStep("missing.capability"),)), approved=True)
    assert result.status == "invalid"
    assert "missing.capability" in result.steps[0].message
