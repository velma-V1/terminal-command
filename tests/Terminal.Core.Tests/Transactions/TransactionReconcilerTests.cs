using Terminal.Core.Transactions;

namespace Terminal.Core.Tests.Transactions;

public sealed class TransactionReconcilerTests
{
    [Fact]
    public async Task Startup_reconciliation_marks_post_start_unknown_work_indeterminate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.Parse("2026-09-01T20:00:00Z");
        var started = new TransactionRecord(Guid.NewGuid(), Guid.NewGuid(), TransactionState.Started, now.AddMinutes(-2), now.AddMinutes(-1));
        var authorized = new TransactionRecord(Guid.NewGuid(), Guid.NewGuid(), TransactionState.Authorized, now.AddMinutes(-2), now.AddMinutes(-1));
        var journal = new FakeJournal(started, authorized);

        var reconciled = await TransactionReconciler.ReconcileAsync(journal, now, cancellationToken);

        Assert.Contains(started.TransactionId, reconciled);
        Assert.DoesNotContain(authorized.TransactionId, reconciled);
        Assert.Equal(TransactionState.Indeterminate, (await journal.GetAsync(started.TransactionId, cancellationToken))!.Value.State);
        Assert.Equal(TransactionState.Authorized, (await journal.GetAsync(authorized.TransactionId, cancellationToken))!.Value.State);
    }

    private sealed class FakeJournal(params TransactionRecord[] records) : ITransactionJournal
    {
        private readonly Dictionary<Guid, TransactionRecord> _records = records.ToDictionary(static record => record.TransactionId);

        public ValueTask<TransactionRecord> CreateAsync(Guid transactionId, Guid actionId, DateTimeOffset now, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<TransactionRecord> TransitionAsync(Guid transactionId, TransactionState to, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = _records[transactionId];
            TransactionStateMachine.Transition(current.State, to);
            var updated = current with { State = to, UpdatedAt = now };
            _records[transactionId] = updated;
            return ValueTask.FromResult(updated);
        }

        public ValueTask<TransactionRecord?> GetAsync(Guid transactionId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_records.TryGetValue(transactionId, out var value) ? (TransactionRecord?)value : null);
        }

        public ValueTask<IReadOnlyList<TransactionEventRecord>> ListEventsAsync(Guid transactionId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<TransactionEventRecord>>([]);

        public ValueTask<IReadOnlyList<TransactionRecord>> ListIncompleteAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<TransactionRecord>>(_records.Values.ToArray());
        }
    }
}
