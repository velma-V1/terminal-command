using Terminal.Core.Actions;
using Terminal.Core.Authority;
using Terminal.Core.Transactions;

namespace Terminal.Execution;

internal sealed record ExecutionAdmissionResult(
    bool Accepted,
    ExecutionBrokerRejection Rejection,
    ApprovalValidation? ApprovalValidation)
{
    public static ExecutionAdmissionResult Reject(
        ExecutionBrokerRejection rejection,
        ApprovalValidation? approvalValidation = null)
        => new(false, rejection, approvalValidation);

    public static ExecutionAdmissionResult Allow(ApprovalValidation? approvalValidation)
        => new(true, ExecutionBrokerRejection.None, approvalValidation);
}

internal sealed class ExecutionAdmissionGate
{
    private readonly IApprovalTicketStore _approvalTickets;
    private readonly ITransactionJournal _journal;
    private readonly ITargetEvidenceResolver _targetEvidenceResolver;
    private readonly TimeProvider _timeProvider;

    public ExecutionAdmissionGate(
        IApprovalTicketStore approvalTickets,
        ITransactionJournal journal,
        ITargetEvidenceResolver targetEvidenceResolver,
        TimeProvider timeProvider)
    {
        _approvalTickets = approvalTickets ?? throw new ArgumentNullException(nameof(approvalTickets));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _targetEvidenceResolver = targetEvidenceResolver ?? throw new ArgumentNullException(nameof(targetEvidenceResolver));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async ValueTask<ExecutionAdmissionResult> AdmitAsync(
        TerminalAction action,
        ExecutionAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(authorization);

        if (!authorization.MatchesAction(action))
        {
            return ExecutionAdmissionResult.Reject(ExecutionBrokerRejection.ActionMismatch);
        }

        var currentTargetEvidence = await _targetEvidenceResolver
            .RevalidateAsync(action, cancellationToken)
            .ConfigureAwait(false);
        if (!authorization.MatchesTarget(currentTargetEvidence))
        {
            return ExecutionAdmissionResult.Reject(ExecutionBrokerRejection.TargetEvidenceMismatch);
        }

        var transaction = await _journal
            .GetAsync(authorization.TransactionId, cancellationToken)
            .ConfigureAwait(false);
        if (transaction is not { } currentTransaction ||
            currentTransaction.ActionId != action.ActionId ||
            currentTransaction.State != TransactionState.Authorized)
        {
            return ExecutionAdmissionResult.Reject(ExecutionBrokerRejection.TransactionNotAuthorized);
        }

        ApprovalValidation? approvalValidation = null;
        switch (authorization.PolicyKind)
        {
            case PolicyDecisionKind.AllowAuto:
                break;

            case PolicyDecisionKind.RequireApproval:
                if (authorization.ApprovalTicketId is not { } ticketId)
                {
                    return ExecutionAdmissionResult.Reject(ExecutionBrokerRejection.ApprovalInvalid);
                }

                var approval = await _approvalTickets
                    .ConsumeAsync(
                        ticketId,
                        authorization.ActionId,
                        authorization.ActionHash,
                        _timeProvider.GetUtcNow(),
                        cancellationToken)
                    .ConfigureAwait(false);
                approvalValidation = approval.Validation;
                if (approval.Validation != ApprovalValidation.Valid)
                {
                    return ExecutionAdmissionResult.Reject(
                        ExecutionBrokerRejection.ApprovalInvalid,
                        approval.Validation);
                }

                break;

            default:
                return ExecutionAdmissionResult.Reject(ExecutionBrokerRejection.UnsupportedPolicy);
        }

        try
        {
            await _journal
                .TransitionAsync(
                    authorization.TransactionId,
                    TransactionState.Started,
                    _timeProvider.GetUtcNow(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return ExecutionAdmissionResult.Reject(
                ExecutionBrokerRejection.TransactionNotAuthorized,
                approvalValidation);
        }

        return ExecutionAdmissionResult.Allow(approvalValidation);
    }
}
