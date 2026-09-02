using Terminal.Core.Authority;
using Terminal.Core.Transactions;

namespace Terminal.Execution;

public sealed record AuthorizedTerminalSessionResult(
    ExecutionBrokerOutcome Outcome,
    ExecutionBrokerRejection Rejection,
    ApprovalValidation? ApprovalValidation,
    ITerminalSession? Session)
{
    public static AuthorizedTerminalSessionResult Reject(
        ExecutionBrokerRejection rejection,
        ApprovalValidation? approvalValidation = null)
        => new(ExecutionBrokerOutcome.Rejected, rejection, approvalValidation, null);

    public static AuthorizedTerminalSessionResult Started(
        ITerminalSession session,
        ApprovalValidation? approvalValidation = null)
        => new(ExecutionBrokerOutcome.Executed, ExecutionBrokerRejection.None, approvalValidation, session);
}

public sealed class AuthorizedTerminalSessionBroker
{
    private readonly TerminalSessionManager _sessions;
    private readonly ExecutionAdmissionGate _admissionGate;
    private readonly ITransactionJournal _journal;
    private readonly TimeProvider _timeProvider;

    public AuthorizedTerminalSessionBroker(
        TerminalSessionManager sessions,
        IApprovalTicketStore approvalTickets,
        ITransactionJournal journal,
        ITargetEvidenceResolver targetEvidenceResolver,
        TimeProvider timeProvider)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _admissionGate = new ExecutionAdmissionGate(
            approvalTickets,
            journal,
            targetEvidenceResolver,
            timeProvider);
    }

    public async ValueTask<AuthorizedTerminalSessionResult> StartAsync(
        TerminalSessionRequest request,
        ExecutionAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authorization);

        var admission = await _admissionGate
            .AdmitAsync(request.Action, authorization, cancellationToken)
            .ConfigureAwait(false);
        if (!admission.Accepted)
        {
            return AuthorizedTerminalSessionResult.Reject(
                admission.Rejection,
                admission.ApprovalValidation);
        }

        try
        {
            var session = await _sessions
                .StartAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return AuthorizedTerminalSessionResult.Started(
                session,
                admission.ApprovalValidation);
        }
        catch
        {
            try
            {
                await _journal
                    .TransitionAsync(
                        authorization.TransactionId,
                        TransactionState.Failed,
                        _timeProvider.GetUtcNow(),
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }

            throw;
        }
    }
}
