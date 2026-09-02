namespace Terminal.Core.Transactions;

public static class TransactionReconciler
{
    private static readonly HashSet<TransactionState> UnknownAfterRestart =
    [
        TransactionState.Started,
        TransactionState.SideEffectObserved,
        TransactionState.Verifying,
        TransactionState.RollingBack,
        TransactionState.Compensating
    ];

    public static async ValueTask<IReadOnlyList<Guid>> ReconcileAsync(
        ITransactionJournal journal,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        var incomplete = await journal.ListIncompleteAsync(cancellationToken).ConfigureAwait(false);
        var reconciled = new List<Guid>();

        foreach (var transaction in incomplete.OrderBy(static item => item.CreatedAt))
        {
            if (!UnknownAfterRestart.Contains(transaction.State) ||
                !TransactionStateMachine.CanTransition(transaction.State, TransactionState.Indeterminate))
            {
                continue;
            }

            await journal
                .TransitionAsync(transaction.TransactionId, TransactionState.Indeterminate, now, cancellationToken)
                .ConfigureAwait(false);
            reconciled.Add(transaction.TransactionId);
        }

        return reconciled.AsReadOnly();
    }
}
