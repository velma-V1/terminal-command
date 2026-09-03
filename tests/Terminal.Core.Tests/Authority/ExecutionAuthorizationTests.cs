using Terminal.Core.Actions;
using Terminal.Core.Authority;
using Terminal.Core.Evidence;

namespace Terminal.Core.Tests.Authority;

public sealed class ExecutionAuthorizationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-01T20:00:00Z");

    [Fact]
    public void Envelope_binds_exact_action_identity_transaction_policy_and_target_evidence()
    {
        var action = CreateAction();
        var transactionId = Guid.NewGuid();
        var evidence = new TargetEvidenceReference(Guid.NewGuid(), 7);
        var policy = new PolicyDecision(PolicyDecisionKind.AllowAuto, "observe.safe", RequiresTargetRevalidation: true);
        var authorization = ExecutionAuthorization.Issue(action, policy, transactionId, evidence, approvalTicketId: null, Now);

        Assert.Equal(action.ActionId, authorization.ActionId);
        Assert.Equal(ActionHash.Compute(action), authorization.ActionHash);
        Assert.Equal(transactionId, authorization.TransactionId);
        Assert.Equal(PolicyDecisionKind.AllowAuto, authorization.PolicyKind);
        Assert.Equal("observe.safe", authorization.PolicyReasonCode);
        Assert.Equal(evidence, authorization.TargetEvidence);
        Assert.True(authorization.MatchesAction(action));
        Assert.True(authorization.MatchesTarget(evidence));
    }

    [Fact]
    public void Materially_changed_action_does_not_match_existing_authorization()
    {
        var action = CreateAction();
        var authorization = ExecutionAuthorization.Issue(
            action,
            new PolicyDecision(PolicyDecisionKind.AllowAuto, "observe.safe"),
            Guid.NewGuid(),
            new TargetEvidenceReference(Guid.NewGuid(), 1),
            approvalTicketId: null,
            Now);
        var changed = CreateAction(operation: "git-diff", actionId: action.ActionId);
        Assert.False(authorization.MatchesAction(changed));
    }

    [Fact]
    public void Require_approval_policy_requires_ticket_identity()
    {
        var action = CreateAction();
        var policy = new PolicyDecision(PolicyDecisionKind.RequireApproval, "privileged");
        Assert.Throws<ArgumentException>(() => ExecutionAuthorization.Issue(action, policy, Guid.NewGuid(), new TargetEvidenceReference(Guid.NewGuid(), 1), approvalTicketId: null, Now));
    }

    [Fact]
    public void Denied_policy_cannot_issue_execution_authorization()
    {
        var action = CreateAction();
        var policy = new PolicyDecision(PolicyDecisionKind.Deny, "catastrophic");
        Assert.Throws<InvalidOperationException>(() => ExecutionAuthorization.Issue(action, policy, Guid.NewGuid(), new TargetEvidenceReference(Guid.NewGuid(), 1), approvalTicketId: null, Now));
    }

    [Fact]
    public void Target_evidence_version_mismatch_is_stale()
    {
        var action = CreateAction();
        var evidenceId = Guid.NewGuid();
        var authorization = ExecutionAuthorization.Issue(
            action,
            new PolicyDecision(PolicyDecisionKind.AllowAuto, "observe.safe", RequiresTargetRevalidation: true),
            Guid.NewGuid(),
            new TargetEvidenceReference(evidenceId, 4),
            approvalTicketId: null,
            Now);
        Assert.False(authorization.MatchesTarget(new TargetEvidenceReference(evidenceId, 5)));
    }

    [Fact]
    public void Required_approval_ticket_is_preserved_exactly()
    {
        var action = CreateAction();
        var ticketId = Guid.NewGuid();
        var authorization = ExecutionAuthorization.Issue(
            action,
            new PolicyDecision(PolicyDecisionKind.RequireApproval, "privileged"),
            Guid.NewGuid(),
            new TargetEvidenceReference(Guid.NewGuid(), 2),
            ticketId,
            Now);
        Assert.Equal(ticketId, authorization.ApprovalTicketId);
    }

    private static TerminalAction CreateAction(string operation = "git", Guid? actionId = null)
        => new(
            actionId: actionId ?? Guid.NewGuid(),
            origin: "terminal",
            capabilityId: "git.status",
            operation: operation,
            arguments: ["status"],
            backend: ActionBackend.Windows,
            workingDirectory: new ResourceRef(ResourceEnvironment.Windows, ResourceKind.Directory, "C:\\repo", "repo", "dir:repo", "windows-host", "generation:1", Now, RevalidationMethod.DirectoryIdentity),
            environmentDelta: new Dictionary<string, string?>(),
            targets:
            [
                new ResourceRef(ResourceEnvironment.Windows, ResourceKind.Repository, "C:\\repo", "repo", "repo:123", "windows-host", "head:abc", Now, RevalidationMethod.RepositoryHead)
            ],
            scope: new ScopeContract([new ScopeEntry(ScopeDimension.FilesystemRead, "C:\\repo")]),
            timeout: TimeSpan.FromSeconds(30),
            memoryLimitBytes: 256 * 1024 * 1024,
            mutation: MutationClass.Observe,
            recovery: RecoveryClass.None,
            provenance: new Provenance(ProvenanceSourceType.User, "user", TrustClass.Authenticated, Now, "evidence:user", []),
            createdAt: Now);
}
