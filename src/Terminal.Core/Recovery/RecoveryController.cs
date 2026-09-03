using Terminal.Core.Evidence;
using Terminal.Core.Intelligence;
using Terminal.Core.Verification;

namespace Terminal.Core.Recovery;

public sealed record CandidateTestResult(
    VerificationOutcome ReproductionVerification,
    VerificationOutcome RegressionVerification,
    bool ScopePreserved,
    bool Retryable);

public interface IRecoveryCandidateTester
{
    ValueTask<CandidateTestResult> TestAsync(
        RecoveryRequest request,
        RecoveryCandidate candidate,
        CancellationToken cancellationToken = default);
}

public interface IVerifiedKnowledgeStore
{
    ValueTask<IReadOnlyList<VerifiedKnowledgeRecord>> FindByTriggerAsync(
        string triggerSignature,
        CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        VerifiedKnowledgeRecord knowledge,
        CancellationToken cancellationToken = default);
}

public enum RecoveryResolutionKind
{
    KnownKnowledge,
    Resumable,
    Failed
}

public sealed record RecoveryResolution(
    RecoveryResolutionKind Kind,
    RecoveryState State,
    VerifiedKnowledgeRecord? Knowledge,
    int ModelCalls,
    int CandidatesTested,
    string Reason);

public sealed class RecoveryController
{
    private readonly ModelEscalationRouter _models;
    private readonly IRecoveryCandidateTester _tester;
    private readonly IVerifiedKnowledgeStore _knowledge;
    private readonly TimeProvider _timeProvider;
    private readonly int _maxModelOutputTokens;

    public RecoveryController(
        ModelEscalationRouter models,
        IRecoveryCandidateTester tester,
        IVerifiedKnowledgeStore knowledge,
        TimeProvider timeProvider,
        int maxModelOutputTokens)
    {
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _tester = tester ?? throw new ArgumentNullException(nameof(tester));
        _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        if (maxModelOutputTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxModelOutputTokens));
        }

        _maxModelOutputTokens = maxModelOutputTokens;
    }

    public async ValueTask<RecoveryResolution> ResolveAsync(
        RecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var known = await _knowledge
            .FindByTriggerAsync(request.FailureSignature, cancellationToken)
            .ConfigureAwait(false);
        var reusable = SelectReusableKnowledge(known);
        if (reusable is not null)
        {
            return new RecoveryResolution(
                RecoveryResolutionKind.KnownKnowledge,
                RecoveryState.Blocked,
                reusable,
                ModelCalls: 0,
                CandidatesTested: 0,
                "verified-knowledge-available");
        }

        if (!_models.HasAvailableProvider)
        {
            return Failed(RecoveryState.Failed, 0, 0, "no-allowed-model-provider");
        }

        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budgetCts.CancelAfter(request.Budget.MaxDuration);
        var token = budgetCts.Token;

        var state = RecoveryStateMachine.Transition(
            RecoveryState.Blocked,
            RecoveryState.ResearchRequested);
        var modelCalls = 0;
        var candidatesTested = 0;
        string lastReason = "recovery-budget-exhausted";

        try
        {
            while (modelCalls < request.Budget.MaxModelCalls &&
                   candidatesTested < request.Budget.MaxCandidates)
            {
                token.ThrowIfCancellationRequested();
                var response = await _models
                    .CompleteAsync(request.ToModelRequest(_maxModelOutputTokens), token)
                    .ConfigureAwait(false);
                modelCalls++;
                state = RecoveryStateMachine.Transition(
                    RecoveryState.ResearchRequested,
                    RecoveryState.EvidenceReceived);

                if (!IsTestable(response))
                {
                    lastReason = "model-candidate-incomplete";
                    if (modelCalls >= request.Budget.MaxModelCalls)
                    {
                        state = RecoveryStateMachine.Transition(state, RecoveryState.Failed);
                        break;
                    }

                    state = RecoveryStateMachine.Transition(state, RecoveryState.ResearchRequested);
                    continue;
                }

                var candidate = ToCandidate(request, response, _timeProvider.GetUtcNow());
                state = RecoveryStateMachine.Transition(
                    RecoveryState.EvidenceReceived,
                    RecoveryState.CandidateTesting);
                var result = await _tester
                    .TestAsync(request, candidate, token)
                    .ConfigureAwait(false);
                candidatesTested++;

                if (RecoveryPromotionGate.CanPromote(
                        candidate,
                        result.ReproductionVerification,
                        result.RegressionVerification,
                        result.ScopePreserved))
                {
                    state = RecoveryStateMachine.Transition(
                        RecoveryState.CandidateTesting,
                        RecoveryState.Verified);
                    var promoted = RecoveryPromotionGate.Promote(
                        candidate,
                        result.ReproductionVerification,
                        result.RegressionVerification,
                        result.ScopePreserved,
                        _timeProvider.GetUtcNow());
                    await _knowledge.SaveAsync(promoted, token).ConfigureAwait(false);
                    state = RecoveryStateMachine.Transition(
                        RecoveryState.Verified,
                        RecoveryState.Resumable);
                    return new RecoveryResolution(
                        RecoveryResolutionKind.Resumable,
                        state,
                        promoted,
                        modelCalls,
                        candidatesTested,
                        "candidate-independently-verified");
                }

                lastReason = PromotionFailureReason(result);
                var canRetry = result.Retryable &&
                               modelCalls < request.Budget.MaxModelCalls &&
                               candidatesTested < request.Budget.MaxCandidates;
                if (!canRetry)
                {
                    state = RecoveryStateMachine.Transition(state, RecoveryState.Failed);
                    break;
                }

                state = RecoveryStateMachine.Transition(
                    RecoveryState.CandidateTesting,
                    RecoveryState.ResearchRequested);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Failed(RecoveryState.Failed, modelCalls, candidatesTested, "recovery-duration-budget-exhausted");
        }
        catch (HttpRequestException)
        {
            return Failed(RecoveryState.Failed, modelCalls, candidatesTested, "model-provider-unavailable");
        }
        catch (InvalidDataException)
        {
            return Failed(RecoveryState.Failed, modelCalls, candidatesTested, "model-response-invalid");
        }

        return Failed(state, modelCalls, candidatesTested, lastReason);
    }

    private static VerifiedKnowledgeRecord? SelectReusableKnowledge(
        IReadOnlyList<VerifiedKnowledgeRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        return records
            .Where(static record => record.TrustClass == KnowledgeTrustClass.Verified)
            .OrderByDescending(static record => record.PromotedAt)
            .ThenBy(static record => record.KnowledgeId)
            .FirstOrDefault();
    }

    private static bool IsTestable(ModelResponse response)
        => response.OutputClass == ModelOutputClass.UntrustedCandidate &&
           !response.Authoritative &&
           response.Evidence.Count > 0 &&
           response.ProposedSteps.Count > 0;

    private static RecoveryCandidate ToCandidate(
        RecoveryRequest request,
        ModelResponse response,
        DateTimeOffset observedAt)
        => new(
            Guid.NewGuid(),
            request.FailureSignature,
            KnowledgeKind.Recipe,
            response.Summary,
            response.Evidence,
            response.ProposedSteps,
            new Provenance(
                ProvenanceSourceType.Model,
                response.ProviderId,
                TrustClass.ModelGenerated,
                observedAt,
                $"model-response:{response.ProviderId}:{response.ResponseId}",
                ["strict-structured-response", "zero-authority-candidate"]));

    private static string PromotionFailureReason(CandidateTestResult result)
    {
        if (!result.ScopePreserved)
        {
            return "candidate-expanded-scope";
        }

        if (!result.ReproductionVerification.IsFullSuccess())
        {
            return "candidate-failed-reproduction-verification";
        }

        if (!result.RegressionVerification.IsFullSuccess())
        {
            return "candidate-failed-regression-verification";
        }

        return "candidate-not-promotable";
    }

    private static RecoveryResolution Failed(
        RecoveryState state,
        int modelCalls,
        int candidatesTested,
        string reason)
        => new(
            RecoveryResolutionKind.Failed,
            state,
            Knowledge: null,
            modelCalls,
            candidatesTested,
            reason);
}
