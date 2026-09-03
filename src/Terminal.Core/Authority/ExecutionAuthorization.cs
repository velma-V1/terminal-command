using Terminal.Core.Actions;

namespace Terminal.Core.Authority;

public readonly record struct TargetEvidenceReference
{
    public TargetEvidenceReference(Guid evidenceId, long version)
    {
        if (evidenceId == Guid.Empty)
        {
            throw new ArgumentException("Target evidence ID must not be empty.", nameof(evidenceId));
        }

        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Target evidence version must be positive.");
        }

        EvidenceId = evidenceId;
        Version = version;
    }

    public Guid EvidenceId { get; }
    public long Version { get; }
}

public sealed record ExecutionAuthorization
{
    private ExecutionAuthorization(
        Guid actionId,
        string actionHash,
        Guid transactionId,
        PolicyDecisionKind policyKind,
        string policyReasonCode,
        bool requiresTargetRevalidation,
        Guid? approvalTicketId,
        TargetEvidenceReference targetEvidence,
        DateTimeOffset issuedAt)
    {
        ActionId = actionId;
        ActionHash = actionHash;
        TransactionId = transactionId;
        PolicyKind = policyKind;
        PolicyReasonCode = policyReasonCode;
        RequiresTargetRevalidation = requiresTargetRevalidation;
        ApprovalTicketId = approvalTicketId;
        TargetEvidence = targetEvidence;
        IssuedAt = issuedAt;
    }

    public Guid ActionId { get; }
    public string ActionHash { get; }
    public Guid TransactionId { get; }
    public PolicyDecisionKind PolicyKind { get; }
    public string PolicyReasonCode { get; }
    public bool RequiresTargetRevalidation { get; }
    public Guid? ApprovalTicketId { get; }
    public TargetEvidenceReference TargetEvidence { get; }
    public DateTimeOffset IssuedAt { get; }

    public static ExecutionAuthorization Issue(
        TerminalAction action,
        PolicyDecision policy,
        Guid transactionId,
        TargetEvidenceReference targetEvidence,
        Guid? approvalTicketId,
        DateTimeOffset issuedAt)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(policy);

        if (transactionId == Guid.Empty)
        {
            throw new ArgumentException("Transaction ID must not be empty.", nameof(transactionId));
        }

        if (string.IsNullOrWhiteSpace(policy.ReasonCode))
        {
            throw new ArgumentException("Policy reason code must not be empty.", nameof(policy));
        }

        if (policy.Kind == PolicyDecisionKind.Deny)
        {
            throw new InvalidOperationException("Denied policy decisions cannot issue execution authorization.");
        }

        if (policy.Kind == PolicyDecisionKind.RequireApproval &&
            approvalTicketId is not { } ticketId)
        {
            throw new ArgumentException(
                "Approval-required policy must bind an approval ticket.",
                nameof(approvalTicketId));
        }

        if (approvalTicketId == Guid.Empty)
        {
            throw new ArgumentException("Approval ticket ID must not be empty.", nameof(approvalTicketId));
        }

        if (policy.Kind == PolicyDecisionKind.AllowAuto && approvalTicketId is not null)
        {
            throw new ArgumentException(
                "Automatic policy decisions must not carry an approval ticket.",
                nameof(approvalTicketId));
        }

        return new ExecutionAuthorization(
            action.ActionId,
            Actions.ActionHash.Compute(action),
            transactionId,
            policy.Kind,
            policy.ReasonCode,
            policy.RequiresTargetRevalidation,
            approvalTicketId,
            targetEvidence,
            issuedAt);
    }

    public bool MatchesAction(TerminalAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ActionId == action.ActionId &&
               string.Equals(ActionHash, Actions.ActionHash.Compute(action), StringComparison.Ordinal);
    }

    public bool MatchesTarget(TargetEvidenceReference currentEvidence)
        => TargetEvidence == currentEvidence;
}
