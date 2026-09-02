using Terminal.Core.Actions;
using Terminal.Core.Authority;
using Terminal.Core.Evidence;
using Terminal.Core.Transactions;
using Terminal.Execution;

namespace Terminal.Execution.Tests;

public sealed class ExecutionBrokerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-01T20:00:00Z");

    [Fact]
    public async Task Changed_action_is_rejected_before_target_or_process_execution()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var action = CreateAction();
        var changed = CreateAction(operation: "git-diff", actionId: action.ActionId);
        var evidence = new TargetEvidenceReference(Guid.NewGuid(), 1);
        var transactionId = Guid.NewGuid();
        var journal = new FakeJournal(transactionId, action.ActionId, TransactionState.Authorized);
        var tickets = new InMemoryApprovalTicketStore();
        var resolver = new FakeTargetEvidenceResolver(evidence);
        var supervisor = new RecordingProcessSupervisor(journal);
        var broker = CreateBroker(supervisor, tickets, journal, resolver);
        var authorization = ExecutionAuthorization.Issue(action, new PolicyDecision(PolicyDecisionKind.AllowAuto, "observe.safe"), transactionId, evidence, approvalTicketId: null, Now);

        var result = await broker.ExecuteAsync(changed, authorization, cancellationToken);

        Assert.Equal(ExecutionBrokerOutcome.Rejected, result.Outcome);
        Assert.Equal(ExecutionBrokerRejection.ActionMismatch, result.Rejection);
        Assert.Equal(0, resolver.Calls);
        Assert.Equal(0, supervisor.Calls);
    }

    [Fact]
    public async Task Stale_target_evidence_is_rejected_before_process_execution()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var action = CreateAction();
        var authorizedEvidence = new TargetEvidenceReference(Guid.NewGuid(), 2);
        var currentEvidence = new TargetEvidenceReference(authorizedEvidence.EvidenceId, 3);
        var transactionId = Guid.NewGuid();
        var journal = new FakeJournal(transactionId, action.ActionId, TransactionState.Authorized);
        var resolver = new FakeTargetEvidenceResolver(currentEvidence);
        var supervisor = new RecordingProcessSupervisor(journal);
        var broker = CreateBroker(supervisor, new InMemoryApprovalTicketStore(), journal, resolver);
        var authorization = ExecutionAuthorization.Issue(action, new PolicyDecision(PolicyDecisionKind.AllowAuto, "observe.safe", RequiresTargetRevalidation: true), transactionId, authorizedEvidence, approvalTicketId: null, Now);

        var result = await broker.ExecuteAsync(action, authorization, cancellationToken);

        Assert.Equal(ExecutionBrokerOutcome.Rejected, result.Outcome);
        Assert.Equal(ExecutionBrokerRejection.TargetEvidenceMismatch, result.Rejection);
        Assert.Equal(0, supervisor.Calls);
    }

    [Fact]
    public async Task Missing_required_approval_is_rejected_before_transaction_start()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var action = CreateAction();
        var evidence = new TargetEvidenceReference(Guid.NewGuid(), 1);
        var transactionId = Guid.NewGuid();
        var journal = new FakeJournal(transactionId, action.ActionId, TransactionState.Authorized);
        var supervisor = new RecordingProcessSupervisor(journal);
        var broker = CreateBroker(supervisor, new InMemoryApprovalTicketStore(), journal, new FakeTargetEvidenceResolver(evidence));
        var authorization = ExecutionAuthorization.Issue(action, new PolicyDecision(PolicyDecisionKind.RequireApproval, "privileged"), transactionId, evidence, Guid.NewGuid(), Now);

        var result = await broker.ExecuteAsync(action, authorization, cancellationToken);

        Assert.Equal(ExecutionBrokerOutcome.Rejected, result.Outcome);
        Assert.Equal(ExecutionBrokerRejection.ApprovalInvalid, result.Rejection);
        Assert.Equal(ApprovalValidation.NotFound, result.ApprovalValidation);
        Assert.Equal(TransactionState.Authorized, journal.Current.State);
        Assert.Equal(0, supervisor.Calls);
    }

    [Fact]
    public async Task Transaction_must_be_authorized_before_process_execution()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var action = CreateAction();
        var evidence = new TargetEvidenceReference(Guid.NewGuid(), 1);
        var transactionId = Guid.NewGuid();
        var journal = new FakeJournal(transactionId, action.ActionId, TransactionState.Prepared);
        var supervisor = new RecordingProcessSupervisor(journal);
        var broker = CreateBroker(supervisor, new InMemoryApprovalTicketStore(), journal, new FakeTargetEvidenceResolver(evidence));
        var authorization = ExecutionAuthorization.Issue(action, new PolicyDecision(PolicyDecisionKind.AllowAuto, "observe.safe"), transactionId, evidence, approvalTicketId: null, Now);

        var result = await broker.ExecuteAsync(action, authorization, cancellationToken);

        Assert.Equal(ExecutionBrokerOutcome.Rejected, result.Outcome);
        Assert.Equal(ExecutionBrokerRejection.TransactionNotAuthorized, result.Rejection);
        Assert.Equal(0, supervisor.Calls);
    }

    [Fact]
    public async Task Valid_auto_authorization_journals_started_before_supervisor_runs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var action = CreateAction();
        var evidence = new TargetEvidenceReference(Guid.NewGuid(), 1);
        var transactionId = Guid.NewGuid();
        var journal = new FakeJournal(transactionId, action.ActionId, TransactionState.Authorized);
        var supervisor = new RecordingProcessSupervisor(journal);
        var broker = CreateBroker(supervisor, new InMemoryApprovalTicketStore(), journal, new FakeTargetEvidenceResolver(evidence));
        var authorization = ExecutionAuthorization.Issue(action, new PolicyDecision(PolicyDecisionKind.AllowAuto, "observe.safe"), transactionId, evidence, approvalTicketId: null, Now);

        var result = await broker.ExecuteAsync(action, authorization, cancellationToken);

        Assert.Equal(ExecutionBrokerOutcome.Executed, result.Outcome);
        Assert.Equal(1, supervisor.Calls);
        Assert.Equal(TransactionState.Started, supervisor.StateObservedAtExecution);
        Assert.Equal(TransactionState.Started, journal.Current.State);
    }

    [Fact]
    public async Task Valid_required_approval_is_consumed_and_executes_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var action = CreateAction();
        var evidence = new TargetEvidenceReference(Guid.NewGuid(), 1);
        var transactionId = Guid.NewGuid();
        var ticket = ApprovalTicket.Issue(action.ActionId, ActionHash.Compute(action), Now, TimeSpan.FromMinutes(5));
        var tickets = new InMemoryApprovalTicketStore();
        await tickets.AddAsync(ticket, cancellationToken);
        var journal = new FakeJournal(transactionId, action.ActionId, TransactionState.Authorized);
        var supervisor = new RecordingProcessSupervisor(journal);
        var broker = CreateBroker(supervisor, tickets, journal, new FakeTargetEvidenceResolver(evidence));
        var authorization = ExecutionAuthorization.Issue(action, new PolicyDecision(PolicyDecisionKind.RequireApproval, "privileged"), transactionId, evidence, ticket.TicketId, Now);

        var result = await broker.ExecuteAsync(action, authorization, cancellationToken);
        var replay = await tickets.ConsumeAsync(ticket.TicketId, action.ActionId, ActionHash.Compute(action), Now.AddSeconds(1), cancellationToken);

        Assert.Equal(ExecutionBrokerOutcome.Executed, result.Outcome);
        Assert.Equal(ApprovalValidation.Valid, result.ApprovalValidation);
        Assert.Equal(ApprovalValidation.Consumed, replay.Validation);
        Assert.Equal(1, supervisor.Calls);
    }

    private static ExecutionBroker CreateBroker(IProcessSupervisor supervisor, IApprovalTicketStore tickets, ITransactionJournal journal, ITargetEvidenceResolver resolver)
        => new(supervisor, tickets, journal, resolver, new FixedTimeProvider(Now));

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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeTargetEvidenceResolver(TargetEvidenceReference evidence) : ITargetEvidenceResolver
    {
        public int Calls { get; private set; }

        public ValueTask<TargetEvidenceReference> RevalidateAsync(TerminalAction action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(action);
            Calls++;
            return ValueTask.FromResult(evidence);
        }
    }

    private sealed class RecordingProcessSupervisor(FakeJournal journal) : IProcessSupervisor
    {
        public int Calls { get; private set; }
        public TransactionState? StateObservedAtExecution { get; private set; }

        public ValueTask<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            StateObservedAtExecution = journal.Current.State;
            return ValueTask.FromResult(new ProcessExecutionResult(request.ExecutionId, ProcessExecutionStatus.Exited, exitCode: 0));
        }
    }

    private sealed class FakeJournal : ITransactionJournal
    {
        public FakeJournal(Guid transactionId, Guid actionId, TransactionState state)
        {
            Current = new TransactionRecord(transactionId, actionId, state, Now, Now);
        }

        public TransactionRecord Current { get; private set; }

        public ValueTask<TransactionRecord> CreateAsync(Guid transactionId, Guid actionId, DateTimeOffset now, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<TransactionRecord> TransitionAsync(Guid transactionId, TransactionState to, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (transactionId != Current.TransactionId)
            {
                throw new KeyNotFoundException();
            }

            TransactionStateMachine.Transition(Current.State, to);
            Current = Current with { State = to, UpdatedAt = now };
            return ValueTask.FromResult(Current);
        }

        public ValueTask<TransactionRecord?> GetAsync(Guid transactionId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TransactionRecord? result = transactionId == Current.TransactionId ? Current : null;
            return ValueTask.FromResult(result);
        }

        public ValueTask<IReadOnlyList<TransactionEventRecord>> ListEventsAsync(Guid transactionId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<TransactionEventRecord>>([]);

        public ValueTask<IReadOnlyList<TransactionRecord>> ListIncompleteAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<TransactionRecord>>([Current]);
    }
}
