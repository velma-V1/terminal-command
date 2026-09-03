using System.Runtime.CompilerServices;
using Terminal.Core.Actions;
using Terminal.Core.Authority;
using Terminal.Core.Evidence;
using Terminal.Core.Transactions;
using Terminal.Execution;

namespace Terminal.Execution.Tests;

public sealed class AuthorizedTerminalSessionBrokerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-01T20:00:00Z");

    [Fact]
    public async Task Changed_action_is_rejected_before_target_revalidation_or_session_launch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var action = CreateAction();
        var changed = CreateAction(operation: "powershell.exe", actionId: action.ActionId);
        var evidence = new TargetEvidenceReference(Guid.NewGuid(), 1);
        var transactionId = Guid.NewGuid();
        var journal = new FakeJournal(transactionId, action.ActionId, TransactionState.Authorized);
        var resolver = new FakeTargetEvidenceResolver(evidence);
        var host = new RecordingSessionHost(journal);
        await using var manager = new TerminalSessionManager([host]);
        var broker = CreateBroker(manager, new InMemoryApprovalTicketStore(), journal, resolver);
        var authorization = ExecutionAuthorization.Issue(
            action,
            new PolicyDecision(PolicyDecisionKind.AllowAuto, "observe.safe"),
            transactionId,
            evidence,
            approvalTicketId: null,
            Now);

        var result = await broker.StartAsync(Request(changed), authorization, cancellationToken);

        Assert.Equal(ExecutionBrokerOutcome.Rejected, result.Outcome);
        Assert.Equal(ExecutionBrokerRejection.ActionMismatch, result.Rejection);
        Assert.Equal(0, resolver.Calls);
        Assert.Equal(0, host.Starts);
    }

    [Fact]
    public async Task Stale_target_evidence_is_rejected_before_session_launch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var action = CreateAction();
        var authorizedEvidence = new TargetEvidenceReference(Guid.NewGuid(), 1);
        var currentEvidence = new TargetEvidenceReference(authorizedEvidence.EvidenceId, 2);
        var transactionId = Guid.NewGuid();
        var journal = new FakeJournal(transactionId, action.ActionId, TransactionState.Authorized);
        var host = new RecordingSessionHost(journal);
        await using var manager = new TerminalSessionManager([host]);
        var broker = CreateBroker(
            manager,
            new InMemoryApprovalTicketStore(),
            journal,
            new FakeTargetEvidenceResolver(currentEvidence));
        var authorization = ExecutionAuthorization.Issue(
            action,
            new PolicyDecision(PolicyDecisionKind.AllowAuto, "observe.safe", RequiresTargetRevalidation: true),
            transactionId,
            authorizedEvidence,
            approvalTicketId: null,
            Now);

        var result = await broker.StartAsync(Request(action), authorization, cancellationToken);

        Assert.Equal(ExecutionBrokerOutcome.Rejected, result.Outcome);
        Assert.Equal(ExecutionBrokerRejection.TargetEvidenceMismatch, result.Rejection);
        Assert.Equal(0, host.Starts);
    }

    [Fact]
    public async Task Missing_required_approval_is_rejected_before_transaction_start_or_session_launch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var action = CreateAction();
        var evidence = new TargetEvidenceReference(Guid.NewGuid(), 1);
        var transactionId = Guid.NewGuid();
        var journal = new FakeJournal(transactionId, action.ActionId, TransactionState.Authorized);
        var host = new RecordingSessionHost(journal);
        await using var manager = new TerminalSessionManager([host]);
        var broker = CreateBroker(
            manager,
            new InMemoryApprovalTicketStore(),
            journal,
            new FakeTargetEvidenceResolver(evidence));
        var authorization = ExecutionAuthorization.Issue(
            action,
            new PolicyDecision(PolicyDecisionKind.RequireApproval, "privileged"),
            transactionId,
            evidence,
            Guid.NewGuid(),
            Now);

        var result = await broker.StartAsync(Request(action), authorization, cancellationToken);

        Assert.Equal(ExecutionBrokerOutcome.Rejected, result.Outcome);
        Assert.Equal(ExecutionBrokerRejection.ApprovalInvalid, result.Rejection);
        Assert.Equal(ApprovalValidation.NotFound, result.ApprovalValidation);
        Assert.Equal(TransactionState.Authorized, journal.Current.State);
        Assert.Equal(0, host.Starts);
    }

    [Fact]
    public async Task Valid_auto_authorization_transitions_started_before_session_host_runs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var action = CreateAction();
        var evidence = new TargetEvidenceReference(Guid.NewGuid(), 1);
        var transactionId = Guid.NewGuid();
        var journal = new FakeJournal(transactionId, action.ActionId, TransactionState.Authorized);
        var host = new RecordingSessionHost(journal);
        await using var manager = new TerminalSessionManager([host]);
        var broker = CreateBroker(
            manager,
            new InMemoryApprovalTicketStore(),
            journal,
            new FakeTargetEvidenceResolver(evidence));
        var authorization = ExecutionAuthorization.Issue(
            action,
            new PolicyDecision(PolicyDecisionKind.AllowAuto, "observe.safe"),
            transactionId,
            evidence,
            approvalTicketId: null,
            Now);

        var result = await broker.StartAsync(Request(action), authorization, cancellationToken);

        Assert.Equal(ExecutionBrokerOutcome.Executed, result.Outcome);
        Assert.NotNull(result.Session);
        Assert.Equal(1, host.Starts);
        Assert.Equal(TransactionState.Started, host.StateObservedAtStart);
        Assert.Equal(TransactionState.Started, journal.Current.State);
    }

    [Fact]
    public async Task Valid_required_approval_is_consumed_once_before_session_launch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var action = CreateAction();
        var evidence = new TargetEvidenceReference(Guid.NewGuid(), 1);
        var transactionId = Guid.NewGuid();
        var ticket = ApprovalTicket.Issue(action.ActionId, ActionHash.Compute(action), Now, TimeSpan.FromMinutes(5));
        var tickets = new InMemoryApprovalTicketStore();
        await tickets.AddAsync(ticket, cancellationToken);
        var journal = new FakeJournal(transactionId, action.ActionId, TransactionState.Authorized);
        var host = new RecordingSessionHost(journal);
        await using var manager = new TerminalSessionManager([host]);
        var broker = CreateBroker(manager, tickets, journal, new FakeTargetEvidenceResolver(evidence));
        var authorization = ExecutionAuthorization.Issue(
            action,
            new PolicyDecision(PolicyDecisionKind.RequireApproval, "privileged"),
            transactionId,
            evidence,
            ticket.TicketId,
            Now);

        var result = await broker.StartAsync(Request(action), authorization, cancellationToken);
        var replay = await tickets.ConsumeAsync(
            ticket.TicketId,
            action.ActionId,
            ActionHash.Compute(action),
            Now.AddSeconds(1),
            cancellationToken);

        Assert.Equal(ExecutionBrokerOutcome.Executed, result.Outcome);
        Assert.Equal(ApprovalValidation.Valid, result.ApprovalValidation);
        Assert.Equal(ApprovalValidation.Consumed, replay.Validation);
        Assert.Equal(1, host.Starts);
    }

    private static AuthorizedTerminalSessionBroker CreateBroker(
        TerminalSessionManager manager,
        IApprovalTicketStore tickets,
        ITransactionJournal journal,
        ITargetEvidenceResolver resolver)
        => new(manager, tickets, journal, resolver, new FixedTimeProvider(Now));

    private static TerminalSessionRequest Request(TerminalAction action)
        => new(Guid.NewGuid(), action, TerminalSessionMode.Foreground, new TerminalDimensions(100, 40));

    private static TerminalAction CreateAction(string operation = "cmd.exe", Guid? actionId = null)
        => new(
            actionId: actionId ?? Guid.NewGuid(),
            origin: "terminal",
            capabilityId: "terminal.session",
            operation: operation,
            arguments: ["/c", "echo ready"],
            backend: ActionBackend.Windows,
            workingDirectory: new ResourceRef(
                ResourceEnvironment.Windows,
                ResourceKind.Directory,
                "C:\\repo",
                "repo",
                "dir:repo",
                "windows-host",
                "generation:1",
                Now,
                RevalidationMethod.DirectoryIdentity),
            environmentDelta: new Dictionary<string, string?>(),
            targets:
            [
                new ResourceRef(
                    ResourceEnvironment.Windows,
                    ResourceKind.Repository,
                    "C:\\repo",
                    "repo",
                    "repo:123",
                    "windows-host",
                    "head:abc",
                    Now,
                    RevalidationMethod.RepositoryHead)
            ],
            scope: new ScopeContract([new ScopeEntry(ScopeDimension.FilesystemRead, "C:\\repo")]),
            timeout: TimeSpan.FromSeconds(30),
            memoryLimitBytes: 256 * 1024 * 1024,
            mutation: MutationClass.Observe,
            recovery: RecoveryClass.None,
            provenance: new Provenance(
                ProvenanceSourceType.User,
                "user",
                TrustClass.Authenticated,
                Now,
                "evidence:user",
                []),
            createdAt: Now);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeTargetEvidenceResolver(TargetEvidenceReference evidence) : ITargetEvidenceResolver
    {
        public int Calls { get; private set; }

        public ValueTask<TargetEvidenceReference> RevalidateAsync(
            TerminalAction action,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(action);
            Calls++;
            return ValueTask.FromResult(evidence);
        }
    }

    private sealed class RecordingSessionHost(FakeJournal journal) : ITerminalSessionHost
    {
        public IReadOnlySet<ActionBackend> SupportedBackends { get; } =
            new HashSet<ActionBackend> { ActionBackend.Windows };
        public int Starts { get; private set; }
        public TransactionState? StateObservedAtStart { get; private set; }

        public ValueTask<ITerminalSession> StartAsync(
            TerminalSessionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Starts++;
            StateObservedAtStart = journal.Current.State;
            return ValueTask.FromResult<ITerminalSession>(new FakeSession(request.SessionId));
        }
    }

    private sealed class FakeSession(Guid sessionId) : ITerminalSession
    {
        public Guid SessionId { get; } = sessionId;
        public ActionBackend Backend => ActionBackend.Windows;
        public TerminalSessionFeatures Features { get; } = new(true, true, true, true);
        public TerminalSessionState State { get; private set; } = TerminalSessionState.Running;

        public ValueTask WriteAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask ResizeAsync(TerminalDimensions dimensions, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask SignalAsync(TerminalSignal signal, CancellationToken cancellationToken = default)
        {
            if (signal == TerminalSignal.Terminate)
            {
                State = TerminalSessionState.Closed;
            }
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadOutputAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask DisposeAsync()
        {
            State = TerminalSessionState.Closed;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeJournal : ITransactionJournal
    {
        public FakeJournal(Guid transactionId, Guid actionId, TransactionState state)
        {
            Current = new TransactionRecord(transactionId, actionId, state, Now, Now);
        }

        public TransactionRecord Current { get; private set; }

        public ValueTask<TransactionRecord> CreateAsync(
            Guid transactionId,
            Guid actionId,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<TransactionRecord> TransitionAsync(
            Guid transactionId,
            TransactionState to,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
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

        public ValueTask<TransactionRecord?> GetAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TransactionRecord? result = transactionId == Current.TransactionId ? Current : null;
            return ValueTask.FromResult(result);
        }

        public ValueTask<IReadOnlyList<TransactionEventRecord>> ListEventsAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<TransactionEventRecord>>([]);

        public ValueTask<IReadOnlyList<TransactionRecord>> ListIncompleteAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<TransactionRecord>>([Current]);
    }
}
