namespace Terminal.Core.Transactions;

public readonly record struct TransactionRecord(
    Guid TransactionId,
    Guid ActionId,
    TransactionState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public readonly record struct TransactionEventRecord(
    long EventId,
    Guid TransactionId,
    TransactionState? FromState,
    TransactionState ToState,
    DateTimeOffset OccurredAt);

public interface ITransactionJournal
{
    ValueTask<TransactionRecord> CreateAsync(
        Guid transactionId,
        Guid actionId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    ValueTask<TransactionRecord> TransitionAsync(
        Guid transactionId,
        TransactionState to,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    ValueTask<TransactionRecord?> GetAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<TransactionEventRecord>> ListEventsAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<TransactionRecord>> ListIncompleteAsync(
        CancellationToken cancellationToken = default);
}
