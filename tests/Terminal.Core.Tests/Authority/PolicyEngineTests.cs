using Terminal.Core.Actions;
using Terminal.Core.Authority;
using Terminal.Core.Evidence;

namespace Terminal.Core.Tests.Authority;

public sealed class PolicyEngineTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-01T20:00:00Z");
    private readonly PolicyEngine _engine = new();

    [Fact]
    public void Observation_is_automatic_when_target_is_valid()
    {
        var decision = _engine.Evaluate(Action(MutationClass.Observe), Facts());
        Assert.Equal(PolicyDecisionKind.AllowAuto, decision.Kind);
    }

    [Fact]
    public void Ephemeral_work_is_automatic_when_target_is_valid()
    {
        var decision = _engine.Evaluate(Action(MutationClass.Ephemeral), Facts());
        Assert.Equal(PolicyDecisionKind.AllowAuto, decision.Kind);
    }

    [Fact]
    public void Local_mutation_requires_verifier_and_recovery_for_automatic_authority()
    {
        var action = Action(MutationClass.LocalMutation, recovery: RecoveryClass.Checkpointable);
        Assert.Equal(PolicyDecisionKind.AllowAuto, _engine.Evaluate(action, Facts(verifierAvailable: true, recoveryPrepared: true)).Kind);
        Assert.Equal(PolicyDecisionKind.RequireApproval, _engine.Evaluate(action, Facts(verifierAvailable: false, recoveryPrepared: true)).Kind);
        Assert.Equal(PolicyDecisionKind.RequireApproval, _engine.Evaluate(action, Facts(verifierAvailable: true, recoveryPrepared: false)).Kind);
    }

    [Fact]
    public void Privileged_remote_root_of_trust_and_irreversible_work_require_approval()
    {
        Assert.Equal(PolicyDecisionKind.RequireApproval, _engine.Evaluate(Action(MutationClass.Observe), Facts(privilegeRequired: true)).Kind);
        Assert.Equal(PolicyDecisionKind.RequireApproval, _engine.Evaluate(Action(MutationClass.Observe, backend: ActionBackend.Remote), Facts()).Kind);
        Assert.Equal(PolicyDecisionKind.RequireApproval, _engine.Evaluate(Action(MutationClass.Observe), Facts(rootOfTrustChange: true)).Kind);
        Assert.Equal(PolicyDecisionKind.RequireApproval, _engine.Evaluate(Action(MutationClass.Irreversible, recovery: RecoveryClass.Irreversible), Facts()).Kind);
    }

    [Fact]
    public void Catastrophic_deny_outranks_every_other_condition()
    {
        var decision = _engine.Evaluate(Action(MutationClass.Ephemeral), Facts(catastrophicDenied: true, verifierAvailable: true, recoveryPrepared: true));
        Assert.Equal(PolicyDecisionKind.Deny, decision.Kind);
        Assert.Equal("catastrophic-deny", decision.ReasonCode);
    }

    [Fact]
    public void Stale_or_unrevalidated_target_is_blocked_until_revalidated()
    {
        var decision = _engine.Evaluate(Action(MutationClass.Observe), Facts(targetRevalidated: false));
        Assert.Equal(PolicyDecisionKind.Deny, decision.Kind);
        Assert.True(decision.RequiresTargetRevalidation);
        Assert.Equal("target-revalidation-required", decision.ReasonCode);
    }

    private static TerminalAction Action(MutationClass mutation, RecoveryClass recovery = RecoveryClass.None, ActionBackend backend = ActionBackend.Windows)
        => new(
            Guid.NewGuid(),
            "terminal",
            "test.capability",
            "test-operation",
            [],
            backend,
            Resource(ResourceKind.Directory, "C:\\repo", "dir:repo", RevalidationMethod.DirectoryIdentity),
            new Dictionary<string, string?>(),
            [Resource(ResourceKind.Repository, "C:\\repo", "repo:1", RevalidationMethod.RepositoryHead)],
            new ScopeContract([new ScopeEntry(ScopeDimension.FilesystemRead, "C:\\repo")]),
            TimeSpan.FromSeconds(30),
            64 * 1024 * 1024,
            mutation,
            recovery,
            new Provenance(ProvenanceSourceType.User, "test", TrustClass.Authenticated, Now, "evidence:test", []),
            Now);

    private static ResourceRef Resource(ResourceKind kind, string canonicalIdentity, string stableIdentity, RevalidationMethod revalidationMethod)
        => new(ResourceEnvironment.Windows, kind, canonicalIdentity, canonicalIdentity, stableIdentity, "windows-host", "version:1", Now, revalidationMethod);

    private static PolicyFacts Facts(
        bool targetRevalidated = true,
        bool verifierAvailable = false,
        bool recoveryPrepared = false,
        bool privilegeRequired = false,
        bool rootOfTrustChange = false,
        bool catastrophicDenied = false)
        => new(targetRevalidated, verifierAvailable, recoveryPrepared, privilegeRequired, rootOfTrustChange, catastrophicDenied);
}
