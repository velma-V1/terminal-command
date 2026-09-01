using Terminal.Core.Actions;
using Terminal.Core.Authority;
using Terminal.Core.Transactions;

namespace Terminal.Execution;

public enum ProcessExecutionStatus
{
    Exited,
    Cancelled,
    TimedOut,
    FailedToStart
}

public enum ProcessContainmentBoundary
{
    None,
    WindowsJobObject,
    LinuxCgroupV2,
    LinuxProcessGroup
}

public sealed class ProcessOutput
{
    private readonly byte[] _captured;

    public ProcessOutput(ReadOnlySpan<byte> captured, long totalBytes)
    {
        if (totalBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalBytes));
        }

        if (totalBytes < captured.Length)
        {
            throw new ArgumentException("Total bytes cannot be smaller than captured bytes.", nameof(totalBytes));
        }

        _captured = captured.ToArray();
        TotalBytes = totalBytes;
    }

    public ReadOnlyMemory<byte> Captured => _captured;
    public long TotalBytes { get; }
    public bool Truncated => TotalBytes > _captured.Length;
}

public sealed record ProcessExecutionMetrics(
    ProcessContainmentBoundary Containment,
    long? PeakMemoryBytes = null,
    TimeSpan? UserCpuTime = null,
    TimeSpan? KernelCpuTime = null);

public sealed record ProcessExecutionRequest(
    Guid ExecutionId,
    Guid TransactionId,
    TerminalAction Action);

public sealed record ProcessExecutionResult
{
    public ProcessExecutionResult(
        Guid executionId,
        ProcessExecutionStatus status,
        int? exitCode,
        ProcessOutput? stdout = null,
        ProcessOutput? stderr = null,
        ProcessExecutionMetrics? metrics = null,
        string? errorMessage = null)
    {
        if (executionId == Guid.Empty)
        {
            throw new ArgumentException("Execution ID must not be empty.", nameof(executionId));
        }

        ExecutionId = executionId;
        Status = status;
        ExitCode = exitCode;
        Stdout = stdout;
        Stderr = stderr;
        Metrics = metrics;
        ErrorMessage = errorMessage;
    }

    public Guid ExecutionId { get; }
    public ProcessExecutionStatus Status { get; }
    public int? ExitCode { get; }
    public ProcessOutput? Stdout { get; }
    public ProcessOutput? Stderr { get; }
    public ProcessExecutionMetrics? Metrics { get; }
    public string? ErrorMessage { get; }
}

public interface IProcessSupervisor
{
    ValueTask<ProcessExecutionResult> ExecuteAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken = default);
}

public interface ITargetEvidenceResolver
{
    ValueTask<TargetEvidenceReference> RevalidateAsync(
        TerminalAction action,
        CancellationToken cancellationToken = default);
}

public enum ExecutionBrokerOutcome
{
    Executed,
    Rejected
}

public enum ExecutionBrokerRejection
{
    None,
    ActionMismatch,
    TargetEvidenceMismatch,
    ApprovalInvalid,
    TransactionNotAuthorized,
    UnsupportedPolicy
}

public sealed record ExecutionBrokerResult(
    ExecutionBrokerOutcome Outcome,
    ExecutionBrokerRejection Rejection,
    ApprovalValidation? ApprovalValidation,
    ProcessExecutionResult? ProcessResult)
{
    public static ExecutionBrokerResult Reject(
        ExecutionBrokerRejection reason,
        ApprovalValidation? approvalValidation = null)
        => new(ExecutionBrokerOutcome.Rejected, reason, approvalValidation, null);

    public static ExecutionBrokerResult Executed(
        ProcessExecutionResult processResult,
        ApprovalValidation? approvalValidation = null)
        => new(ExecutionBrokerOutcome.Executed, ExecutionBrokerRejection.None, approvalValidation, processResult);
}

public sealed class ExecutionBroker : IExecutionBroker
{
    private readonly IProcessSupervisor _supervisor;
    private readonly IApprovalTicketStore _approvalTickets;
    private readonly ITransactionJournal _journal;
    private readonly ITargetEvidenceResolver _targetEvidenceResolver;
    private readonly TimeProvider _timeProvider;

    public ExecutionBroker(
        IProcessSupervisor supervisor,
        IApprovalTicketStore approvalTickets,
        ITransactionJournal journal,
        ITargetEvidenceResolver targetEvidenceResolver,
        TimeProvider timeProvider)
    {
        _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
        _approvalTickets = approvalTickets ?? throw new ArgumentNullException(nameof(approvalTickets));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _targetEvidenceResolver = targetEvidenceResolver ?? throw new ArgumentNullException(nameof(targetEvidenceResolver));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async ValueTask<ExecutionBrokerResult> ExecuteAsync(
        TerminalAction action,
        ExecutionAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(authorization);

        if (!authorization.MatchesAction(action))
        {
            return ExecutionBrokerResult.Reject(ExecutionBrokerRejection.ActionMismatch);
        }

        var currentTargetEvidence = await _targetEvidenceResolver
            .RevalidateAsync(action, cancellationToken)
            .ConfigureAwait(false);
        if (!authorization.MatchesTarget(currentTargetEvidence))
        {
            return ExecutionBrokerResult.Reject(ExecutionBrokerRejection.TargetEvidenceMismatch);
        }

        var transaction = await _journal
            .GetAsync(authorization.TransactionId, cancellationToken)
            .ConfigureAwait(false);
        if (transaction is not { } currentTransaction ||
            currentTransaction.ActionId != action.ActionId ||
            currentTransaction.State != TransactionState.Authorized)
        {
            return ExecutionBrokerResult.Reject(ExecutionBrokerRejection.TransactionNotAuthorized);
        }

        ApprovalValidation? approvalValidation = null;
        switch (authorization.PolicyKind)
        {
            case PolicyDecisionKind.AllowAuto:
                break;

            case PolicyDecisionKind.RequireApproval:
                if (authorization.ApprovalTicketId is not { } ticketId)
                {
                    return ExecutionBrokerResult.Reject(ExecutionBrokerRejection.ApprovalInvalid);
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
                    return ExecutionBrokerResult.Reject(
                        ExecutionBrokerRejection.ApprovalInvalid,
                        approval.Validation);
                }

                break;

            default:
                return ExecutionBrokerResult.Reject(ExecutionBrokerRejection.UnsupportedPolicy);
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
            return ExecutionBrokerResult.Reject(ExecutionBrokerRejection.TransactionNotAuthorized, approvalValidation);
        }

        var request = new ProcessExecutionRequest(
            Guid.NewGuid(),
            authorization.TransactionId,
            action);
        var result = await _supervisor
            .ExecuteAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return ExecutionBrokerResult.Executed(result, approvalValidation);
    }
}

public interface IExecutionBroker
{
    ValueTask<ExecutionBrokerResult> ExecuteAsync(
        TerminalAction action,
        ExecutionAuthorization authorization,
        CancellationToken cancellationToken = default);
}
