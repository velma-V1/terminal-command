using Terminal.Core.Evidence;
using Terminal.Core.Intelligence;
using Terminal.Core.Recovery;
using Terminal.Core.Verification;

namespace Terminal.Core.Tests.Recovery;

public sealed class RecoveryControllerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-02T03:00:00Z");

    [Fact]
    public async Task Existing_verified_knowledge_short_circuits_model_and_candidate_testing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var knowledge = Knowledge("failure:known");
        var store = new FakeKnowledgeStore([knowledge]);
        var model = new QueueModelProvider([]);
        var tester = new QueueCandidateTester([]);
        var controller = Controller(store, model, tester);

        var result = await controller.ResolveAsync(Request("failure:known"), cancellationToken);

        Assert.Equal(RecoveryResolutionKind.KnownKnowledge, result.Kind);
        Assert.Equal(RecoveryState.Blocked, result.State);
        Assert.Equal(knowledge.KnowledgeId, result.Knowledge!.KnowledgeId);
        Assert.Equal(0, model.Calls);
        Assert.Equal(0, tester.Calls);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task Model_output_alone_cannot_resume_or_create_knowledge()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeKnowledgeStore([]);
        var model = new QueueModelProvider([Response("candidate-1")]);
        var tester = new QueueCandidateTester([
            new CandidateTestResult(
                VerificationOutcome.Failed,
                VerificationOutcome.Unverified,
                ScopePreserved: true,
                Retryable: false)
        ]);
        var controller = Controller(store, model, tester);

        var result = await controller.ResolveAsync(Request("failure:new"), cancellationToken);

        Assert.Equal(RecoveryResolutionKind.Failed, result.Kind);
        Assert.Equal(RecoveryState.Failed, result.State);
        Assert.Null(result.Knowledge);
        Assert.Equal(1, model.Calls);
        Assert.Equal(1, tester.Calls);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task Partial_regression_verification_blocks_promotion_and_resume()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeKnowledgeStore([]);
        var model = new QueueModelProvider([Response("candidate-1")]);
        var tester = new QueueCandidateTester([
            new CandidateTestResult(
                VerificationOutcome.Verified,
                VerificationOutcome.Partial,
                ScopePreserved: true,
                Retryable: false)
        ]);
        var controller = Controller(store, model, tester);

        var result = await controller.ResolveAsync(Request("failure:new"), cancellationToken);

        Assert.Equal(RecoveryResolutionKind.Failed, result.Kind);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task Scope_expansion_blocks_promotion_even_when_tests_pass()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeKnowledgeStore([]);
        var model = new QueueModelProvider([Response("candidate-1")]);
        var tester = new QueueCandidateTester([
            new CandidateTestResult(
                VerificationOutcome.Verified,
                VerificationOutcome.Verified,
                ScopePreserved: false,
                Retryable: false)
        ]);
        var controller = Controller(store, model, tester);

        var result = await controller.ResolveAsync(Request("failure:new"), cancellationToken);

        Assert.Equal(RecoveryResolutionKind.Failed, result.Kind);
        Assert.Equal(RecoveryState.Failed, result.State);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task Independently_verified_candidate_is_promoted_then_workflow_becomes_resumable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeKnowledgeStore([]);
        var model = new QueueModelProvider([Response("candidate-1")]);
        var tester = new QueueCandidateTester([
            new CandidateTestResult(
                VerificationOutcome.Verified,
                VerificationOutcome.Verified,
                ScopePreserved: true,
                Retryable: false)
        ]);
        var controller = Controller(store, model, tester);

        var result = await controller.ResolveAsync(Request("failure:new"), cancellationToken);

        Assert.Equal(RecoveryResolutionKind.Resumable, result.Kind);
        Assert.Equal(RecoveryState.Resumable, result.State);
        Assert.NotNull(result.Knowledge);
        Assert.Equal(KnowledgeTrustClass.Verified, result.Knowledge.TrustClass);
        Assert.Equal("failure:new", result.Knowledge.TriggerSignature);
        Assert.Equal(1, store.Saves);
        Assert.Single(store.Stored);
    }

    [Fact]
    public async Task Retryable_candidates_are_bounded_by_model_call_and_candidate_budgets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeKnowledgeStore([]);
        var model = new QueueModelProvider([
            Response("candidate-1"),
            Response("candidate-2"),
            Response("candidate-3")
        ]);
        var tester = new QueueCandidateTester([
            RetryableFailure(),
            RetryableFailure(),
            RetryableFailure()
        ]);
        var controller = Controller(store, model, tester);
        var request = Request(
            "failure:bounded",
            new RecoveryBudget(maxModelCalls: 2, maxCandidates: 5, TimeSpan.FromSeconds(30)));

        var result = await controller.ResolveAsync(request, cancellationToken);

        Assert.Equal(RecoveryResolutionKind.Failed, result.Kind);
        Assert.Equal(2, result.ModelCalls);
        Assert.Equal(2, result.CandidatesTested);
        Assert.Equal(2, model.Calls);
        Assert.Equal(2, tester.Calls);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task Empty_model_candidate_never_reaches_tester_or_learning_store()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeKnowledgeStore([]);
        var model = new QueueModelProvider([
            new ModelResponse("qwen3.5-9b", "candidate-empty", "guess", [], [])
        ]);
        var tester = new QueueCandidateTester([]);
        var controller = Controller(store, model, tester);

        var result = await controller.ResolveAsync(Request("failure:empty"), cancellationToken);

        Assert.Equal(RecoveryResolutionKind.Failed, result.Kind);
        Assert.Equal(0, tester.Calls);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task Cancellation_propagates_without_promotion()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.Cancel();
        var store = new FakeKnowledgeStore([]);
        var model = new QueueModelProvider([Response("candidate-1")]);
        var tester = new QueueCandidateTester([]);
        var controller = Controller(store, model, tester);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.ResolveAsync(Request("failure:cancel"), cts.Token).AsTask());

        Assert.Equal(0, store.Saves);
    }

    private static RecoveryController Controller(
        IVerifiedKnowledgeStore knowledge,
        IModelProvider model,
        IRecoveryCandidateTester tester)
        => new(
            new ModelEscalationRouter([model]),
            tester,
            knowledge,
            TimeProvider.System,
            maxModelOutputTokens: 2048);

    private static RecoveryRequest Request(
        string failureSignature,
        RecoveryBudget? budget = null)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            failureSignature,
            ["compiler error", "reproduction captured"],
            ["stay inside repository scope"],
            "original failure no longer reproduces and regression suite remains green",
            budget ?? new RecoveryBudget(1, 1, TimeSpan.FromSeconds(30)));

    private static ModelResponse Response(string responseId)
        => new(
            "qwen3.5-9b",
            responseId,
            "Pin the incompatible dependency.",
            [new ModelEvidenceReference("official-doc", "documentation")],
            ["change package version", "restore", "build", "test"]);

    private static CandidateTestResult RetryableFailure()
        => new(
            VerificationOutcome.Failed,
            VerificationOutcome.Unverified,
            ScopePreserved: true,
            Retryable: true);

    private static VerifiedKnowledgeRecord Knowledge(string trigger)
        => new(
            Guid.NewGuid(),
            KnowledgeKind.Recipe,
            trigger,
            "Known verified recipe.",
            Guid.NewGuid(),
            KnowledgeTrustClass.Verified,
            Now,
            [new ModelEvidenceReference("verified:test", "verification")]);

    private sealed class QueueModelProvider(IReadOnlyList<ModelResponse> responses) : IModelProvider
    {
        private readonly Queue<ModelResponse> _responses = new(responses);

        public int Calls { get; private set; }
        public ModelProviderDescriptor Descriptor { get; } = ModelProviderDescriptor.Qwen35NineB();

        public ValueTask<ModelResponse> CompleteAsync(
            ModelRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("Unexpected model call.");
            }

            return ValueTask.FromResult(_responses.Dequeue());
        }
    }

    private sealed class QueueCandidateTester(IReadOnlyList<CandidateTestResult> results) : IRecoveryCandidateTester
    {
        private readonly Queue<CandidateTestResult> _results = new(results);

        public int Calls { get; private set; }

        public ValueTask<CandidateTestResult> TestAsync(
            RecoveryRequest request,
            RecoveryCandidate candidate,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (_results.Count == 0)
            {
                throw new InvalidOperationException("Unexpected candidate test.");
            }

            return ValueTask.FromResult(_results.Dequeue());
        }
    }

    private sealed class FakeKnowledgeStore(IReadOnlyList<VerifiedKnowledgeRecord> initial) : IVerifiedKnowledgeStore
    {
        public List<VerifiedKnowledgeRecord> Stored { get; } = [.. initial];
        public int Saves { get; private set; }

        public ValueTask<IReadOnlyList<VerifiedKnowledgeRecord>> FindByTriggerAsync(
            string triggerSignature,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<VerifiedKnowledgeRecord> matches = Stored
                .Where(item => string.Equals(item.TriggerSignature, triggerSignature, StringComparison.Ordinal))
                .ToArray();
            return ValueTask.FromResult(matches);
        }

        public ValueTask SaveAsync(
            VerifiedKnowledgeRecord knowledge,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Saves++;
            Stored.Add(knowledge);
            return ValueTask.CompletedTask;
        }
    }
}
