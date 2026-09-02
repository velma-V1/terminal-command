using Terminal.Core.Evidence;
using Terminal.Core.Intelligence;
using Terminal.Core.Verification;

namespace Terminal.Core.Recovery;

public enum RecoveryState
{
    Blocked,
    ResearchRequested,
    EvidenceReceived,
    CandidateTesting,
    Verified,
    Resumable,
    Failed
}

public static class RecoveryStateMachine
{
    private static readonly IReadOnlyDictionary<RecoveryState, HashSet<RecoveryState>> Allowed =
        new Dictionary<RecoveryState, HashSet<RecoveryState>>
        {
            [RecoveryState.Blocked] = new() { RecoveryState.ResearchRequested, RecoveryState.Failed },
            [RecoveryState.ResearchRequested] = new() { RecoveryState.EvidenceReceived, RecoveryState.Failed },
            [RecoveryState.EvidenceReceived] = new() { RecoveryState.CandidateTesting, RecoveryState.ResearchRequested, RecoveryState.Failed },
            [RecoveryState.CandidateTesting] = new() { RecoveryState.Verified, RecoveryState.ResearchRequested, RecoveryState.Failed },
            [RecoveryState.Verified] = new() { RecoveryState.Resumable, RecoveryState.Failed },
            [RecoveryState.Resumable] = new(),
            [RecoveryState.Failed] = new()
        };

    public static bool CanTransition(RecoveryState from, RecoveryState to)
        => Allowed.TryGetValue(from, out var next) && next.Contains(to);

    public static RecoveryState Transition(RecoveryState from, RecoveryState to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Illegal recovery transition: {from} -> {to}.");
        }

        return to;
    }
}

public readonly record struct RecoveryBudget
{
    public RecoveryBudget(int maxModelCalls, int maxCandidates, TimeSpan maxDuration)
    {
        if (maxModelCalls <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxModelCalls));
        }

        if (maxCandidates <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCandidates));
        }

        if (maxDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDuration));
        }

        MaxModelCalls = maxModelCalls;
        MaxCandidates = maxCandidates;
        MaxDuration = maxDuration;
    }

    public int MaxModelCalls { get; }
    public int MaxCandidates { get; }
    public TimeSpan MaxDuration { get; }
}

public sealed record RecoveryRequest
{
    public RecoveryRequest(
        Guid recoveryId,
        Guid workflowId,
        Guid blockedActionId,
        string failureSignature,
        IReadOnlyList<string> evidence,
        IReadOnlyList<string> constraints,
        string successDefinition,
        RecoveryBudget budget)
    {
        if (recoveryId == Guid.Empty || workflowId == Guid.Empty || blockedActionId == Guid.Empty)
        {
            throw new ArgumentException("Recovery, workflow, and blocked Action IDs must be non-empty.");
        }

        RecoveryId = recoveryId;
        WorkflowId = workflowId;
        BlockedActionId = blockedActionId;
        FailureSignature = Required(failureSignature, nameof(failureSignature));
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(constraints);
        Evidence = Array.AsReadOnly(evidence.ToArray());
        Constraints = Array.AsReadOnly(constraints.ToArray());
        SuccessDefinition = Required(successDefinition, nameof(successDefinition));
        Budget = budget;
    }

    public Guid RecoveryId { get; }
    public Guid WorkflowId { get; }
    public Guid BlockedActionId { get; }
    public string FailureSignature { get; }
    public IReadOnlyList<string> Evidence { get; }
    public IReadOnlyList<string> Constraints { get; }
    public string SuccessDefinition { get; }
    public RecoveryBudget Budget { get; }

    public ModelRequest ToModelRequest(int maxOutputTokens)
        => new(
            RecoveryId,
            "terminal-recovery-research",
            FailureSignature,
            Evidence.Concat(Constraints).Append($"SUCCESS: {SuccessDefinition}").ToArray(),
            maxOutputTokens);

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be empty.", name)
            : value;
}

public enum KnowledgeKind
{
    Detector,
    Recipe,
    Invariant,
    RoutingRule
}

public enum KnowledgeTrustClass
{
    Verified
}

public sealed record RecoveryCandidate
{
    public RecoveryCandidate(
        Guid candidateId,
        string failureSignature,
        KnowledgeKind proposedKnowledgeKind,
        string summary,
        IReadOnlyList<ModelEvidenceReference> evidence,
        IReadOnlyList<string> proposedSteps,
        Provenance provenance)
    {
        if (candidateId == Guid.Empty)
        {
            throw new ArgumentException("Candidate ID must not be empty.", nameof(candidateId));
        }

        CandidateId = candidateId;
        FailureSignature = Required(failureSignature, nameof(failureSignature));
        ProposedKnowledgeKind = proposedKnowledgeKind;
        Summary = Required(summary, nameof(summary));
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(proposedSteps);
        if (proposedSteps.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Candidate steps must be non-empty strings.", nameof(proposedSteps));
        }

        Evidence = Array.AsReadOnly(evidence.ToArray());
        ProposedSteps = Array.AsReadOnly(proposedSteps.ToArray());
        Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
    }

    public Guid CandidateId { get; }
    public string FailureSignature { get; }
    public KnowledgeKind ProposedKnowledgeKind { get; }
    public string Summary { get; }
    public IReadOnlyList<ModelEvidenceReference> Evidence { get; }
    public IReadOnlyList<string> ProposedSteps { get; }
    public Provenance Provenance { get; }

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be empty.", name)
            : value;
}

public sealed record VerifiedKnowledgeRecord(
    Guid KnowledgeId,
    KnowledgeKind Kind,
    string TriggerSignature,
    string Content,
    Guid SourceCandidateId,
    KnowledgeTrustClass TrustClass,
    DateTimeOffset PromotedAt,
    IReadOnlyList<ModelEvidenceReference> Evidence);

public static class RecoveryPromotionGate
{
    public static bool CanPromote(
        RecoveryCandidate candidate,
        VerificationOutcome reproductionVerification,
        VerificationOutcome regressionVerification,
        bool scopePreserved)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return scopePreserved &&
               candidate.Evidence.Count > 0 &&
               candidate.ProposedSteps.Count > 0 &&
               reproductionVerification.IsFullSuccess() &&
               regressionVerification.IsFullSuccess();
    }

    public static VerifiedKnowledgeRecord Promote(
        RecoveryCandidate candidate,
        VerificationOutcome reproductionVerification,
        VerificationOutcome regressionVerification,
        bool scopePreserved,
        DateTimeOffset promotedAt)
    {
        if (!CanPromote(candidate, reproductionVerification, regressionVerification, scopePreserved))
        {
            throw new InvalidOperationException("Candidate did not satisfy the verified-learning promotion gate.");
        }

        return new VerifiedKnowledgeRecord(
            Guid.NewGuid(),
            candidate.ProposedKnowledgeKind,
            candidate.FailureSignature,
            candidate.Summary,
            candidate.CandidateId,
            KnowledgeTrustClass.Verified,
            promotedAt,
            candidate.Evidence);
    }
}
