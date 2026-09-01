from terminal_command.contracts import Action, PolicyDecision, RiskLevel
from terminal_command.policy import PolicyEngine


def evaluate(command, *, name="shell", metadata=None):
    return PolicyEngine().evaluate(
        Action(name=name, command=command, metadata=metadata or {})
    )


def test_read_only_command_auto_allows():
    result = evaluate(["git", "status"])
    assert result.decision is PolicyDecision.ALLOW
    assert result.risk is RiskLevel.READ_ONLY


def test_explicit_requires_approval_metadata_is_authoritative():
    result = evaluate(["python", "-m", "safe-looking-tool"], metadata={"requires_approval": True, "read_only": True, "capability_id": "update.prepare"})
    assert result.decision is PolicyDecision.REQUIRE_APPROVAL
    assert result.risk is RiskLevel.MUTATING


def test_mutating_command_requires_approval():
    result = evaluate(["git", "commit", "-m", "change"])
    assert result.decision is PolicyDecision.REQUIRE_APPROVAL
    assert result.risk is RiskLevel.MUTATING


def test_privileged_command_requires_approval():
    result = evaluate(["sudo", "apt", "update"])
    assert result.decision is PolicyDecision.REQUIRE_APPROVAL
    assert result.risk is RiskLevel.PRIVILEGED


def test_unknown_command_requires_approval():
    result = evaluate(["mystery-tool", "do-thing"])
    assert result.decision is PolicyDecision.REQUIRE_APPROVAL
    assert result.risk is RiskLevel.UNKNOWN


def test_catastrophic_command_is_denied_by_default():
    result = evaluate(["rm", "-rf", "/"])
    assert result.decision is PolicyDecision.DENY
    assert result.risk is RiskLevel.CATASTROPHIC
