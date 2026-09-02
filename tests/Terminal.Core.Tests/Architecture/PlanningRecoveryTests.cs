using Terminal.Core.Actions;
using Terminal.Core.Capabilities;
using Terminal.Core.Evidence;
using Terminal.Core.Intelligence;
using Terminal.Core.Recovery;
using Terminal.Core.SystemState;
using Terminal.Core.Verification;

namespace Terminal.Core.Tests.Architecture;

public sealed class PlanningRecoveryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-02T02:00:00Z");

    [Fact]
    public void SystemGraph_rejects_stale_facts_and_cascades_invalidation()
    {
        var graph = new SystemGraph();
        var repo = Repo();
        var head = Fact("repo.head", "abc123", repo, TimeSpan.FromMinutes(5));
        var build = Fact("repo.build", "green", repo, TimeSpan.FromMinutes(5), ["repo.head"]);

        graph.Upsert(head);
        graph.Upsert(build);

        Assert.True(graph.TryGetFresh("repo.build", Now.AddMinutes(4), out _));
        Assert.False(graph.TryGetFresh("repo.build", Now.AddMinutes(6), out _));

        graph.Upsert(build with { ObservedAt = Now.AddMinutes(10) });
        Assert.True(graph.Invalidate("repo.head"));
        Assert.False(graph.TryGetFresh("repo.build", Now.AddMinutes(10), out _));
    }

    [Fact]
    public void Deterministic_planner_builds_known_multistep_plan_without_model_calls()
    {
        var graph = new SystemGraph();
        graph.Upsert(Fact("repo.present", "true", Repo(), TimeSpan.FromHours(1)));
        var catalog = new CapabilityCatalog([
            Capability(
                "restore",
                [new FactRequirement("repo.present", "true")],
                [new FactEffect("deps.restored", "true")]),
            Capability(
                "build",
                [new FactRequirement("deps.restored", "true")],
                [new FactEffect("build.green", "true")]),
            Capability(
                "test",
                [new FactRequirement("build.green", "true")],
                [new FactEffect("tests.green", "true")])
        ]);
        var model = new CountingModelProvider();
        var coordinator = new GoalCoordinator(new DeterministicPlanner(), new ModelEscalationRouter([model]));

        var resolution = coordinator.Resolve(
            new FactRequirement("tests.green", "true"),
            graph.Snapshot(Now),
            catalog);

        Assert.Equal(GoalResolutionKind.DeterministicPlan, resolution.Kind);
        Assert.Equal(["restore", "build", "test"], resolution.Plan!.Steps.Select(static step => step.CapabilityId));
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public void Ambiguous_deterministic_plan_is_not_silently_guessed()
    {
        var graph = new SystemGraph();
        graph.Upsert(Fact("repo.present", "true", Repo(), TimeSpan.FromHours(1)));
        var catalog = new CapabilityCatalog([
            Capability("build.a", [new FactRequirement("repo.present", "true")], [new FactEffect("build.green", "true")]),
            Capability("build.b", [new FactRequirement("repo.present", "true")], [new FactEffect("build.green", "true")])
        ]);

        var result = new DeterministicPlanner().TryPlan(
            new FactRequirement("build.green", "true"),
            graph.Snapshot(Now),
            catalog);

        Assert.Equal(DeterministicPlanStatus.Ambiguous, result.Status);
        Assert.Null(result.Plan);
    }

    [Fact]
    public void Qwen_provider_is_zero_authority_and_only_configured_model_for_now()
    {
        var policy = ModelProviderPolicy.Qwen35NineBOnly();
        var descriptor = policy.AllowedProviders.Single();

        Assert.Equal("qwen3.5-9b", descriptor.ProviderId);
        Assert.False(descriptor.CanExecute);
        Assert.False(descriptor.CanAuthorize);
        Assert.True(policy.IsAllowed("qwen3.5-9b"));
        Assert.False(policy.IsAllowed("other-model"));
    }

    [Fact]
    public void Failed_or_partial_candidate_can_never_be_promoted_to_learned_knowledge()
    {
        var candidate = Candidate();

        Assert.False(RecoveryPromotionGate.CanPromote(candidate, VerificationOutcome.Failed, VerificationOutcome.Verified, scopePreserved: true));
        Assert.False(RecoveryPromotionGate.CanPromote(candidate, VerificationOutcome.Verified, VerificationOutcome.Partial, scopePreserved: true));
        Assert.False(RecoveryPromotionGate.CanPromote(candidate, VerificationOutcome.Verified, VerificationOutcome.Verified, scopePreserved: false));
    }

    [Fact]
    public void Independently_verified_candidate_promotes_to_reusable_knowledge()
    {
        var candidate = Candidate();

        var promoted = RecoveryPromotionGate.Promote(
            candidate,
            VerificationOutcome.Verified,
            VerificationOutcome.Verified,
            scopePreserved: true,
            promotedAt: Now);

        Assert.Equal(KnowledgeKind.Recipe, promoted.Kind);
        Assert.Equal(candidate.FailureSignature, promoted.TriggerSignature);
        Assert.Equal(candidate.CandidateId, promoted.SourceCandidateId);
        Assert.Equal(KnowledgeTrustClass.Verified, promoted.TrustClass);
    }

    [Fact]
    public void Model_output_remains_candidate_evidence_not_terminal_authority()
    {
        var response = new ModelResponse(
            "qwen3.5-9b",
            "candidate-1",
            "possible dependency mismatch",
            [new ModelEvidenceReference("https://example.invalid/issue", "issue")],
            ["pin package to compatible version"]);

        Assert.False(response.Authoritative);
        Assert.Equal(ModelOutputClass.UntrustedCandidate, response.OutputClass);
    }

    private static SystemFact Fact(
        string key,
        string value,
        ResourceRef subject,
        TimeSpan maxAge,
        IReadOnlyList<string>? dependencies = null)
        => new(
            key,
            subject,
            value,
            new Provenance(
                ProvenanceSourceType.SystemProbe,
                "test-probe",
                "1",
                ProvenanceTrustClass.SystemObserved,
                Now,
                "evidence:test",
                null),
            Now,
            maxAge,
            generation: 1,
            dependencies ?? []);

    private static CapabilityManifest Capability(
        string id,
        IReadOnlyList<FactRequirement> preconditions,
        IReadOnlyList<FactEffect> effects)
        => new(
            id,
            version: "1",
            preconditions,
            effects,
            ActionBackend.Windows,
            AutonomyTier.T0Observe,
            RecoveryClass.None,
            verifierId: "test-verifier",
            new ScopeContract([]),
            new ResourceBudget(TimeSpan.FromMinutes(1), 128 * 1024 * 1024, 1));

    private static RecoveryCandidate Candidate()
        => new(
            Guid.NewGuid(),
            "NU1605:package-version-conflict",
            KnowledgeKind.Recipe,
            "Pin package A to a compatible version.",
            [new ModelEvidenceReference("https://example.invalid/issue", "issue")],
            ["edit project file"],
            new Provenance(
                ProvenanceSourceType.Model,
                "qwen3.5-9b",
                "local",
                ProvenanceTrustClass.UntrustedExternal,
                Now,
                "model-response:test",
                null));

    private static ResourceRef Repo()
        => new(
            ResourceEnvironment.Windows,
            ResourceKind.Repository,
            "C:\\repo",
            "C:\\repo",
            "repo:test",
            "windows:test",
            "abc123",
            Now,
            RevalidationMethod.RepositoryHead);

    private sealed class CountingModelProvider : IModelProvider
    {
        public int Calls { get; private set; }
        public ModelProviderDescriptor Descriptor { get; } = ModelProviderDescriptor.Qwen35NineB();

        public ValueTask<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return ValueTask.FromResult(new ModelResponse(
                Descriptor.ProviderId,
                "unused",
                "unused",
                [],
                []));
        }
    }
}
