namespace Terminal.Core.Transactions;

public static class TransactionStateMachine
{
    private static readonly IReadOnlyDictionary<TransactionState, HashSet<TransactionState>> Allowed =
        new Dictionary<TransactionState, HashSet<TransactionState>>
        {
            [TransactionState.Prepared] = new() { TransactionState.Authorized, TransactionState.Failed, TransactionState.Cancelled },
            [TransactionState.Authorized] = new() { TransactionState.Started, TransactionState.Failed, TransactionState.Cancelled },
            [TransactionState.Started] = new() { TransactionState.SideEffectObserved, TransactionState.Verifying, TransactionState.Failed, TransactionState.Cancelled, TransactionState.Indeterminate },
            [TransactionState.SideEffectObserved] = new() { TransactionState.Verifying, TransactionState.Failed, TransactionState.Cancelled, TransactionState.Indeterminate },
            [TransactionState.Verifying] = new() { TransactionState.Committed, TransactionState.Failed, TransactionState.Indeterminate },
            [TransactionState.Failed] = new() { TransactionState.RollingBack, TransactionState.Compensating },
            [TransactionState.Cancelled] = new() { TransactionState.RollingBack, TransactionState.Compensating },
            [TransactionState.Indeterminate] = new() { TransactionState.RollingBack, TransactionState.Compensating },
            [TransactionState.RollingBack] = new() { TransactionState.RolledBack, TransactionState.RollbackFailed, TransactionState.Indeterminate },
            [TransactionState.Compensating] = new() { TransactionState.Compensated, TransactionState.CompensationFailed, TransactionState.Indeterminate },
            [TransactionState.Committed] = new(),
            [TransactionState.RolledBack] = new(),
            [TransactionState.RollbackFailed] = new(),
            [TransactionState.Compensated] = new(),
            [TransactionState.CompensationFailed] = new()
        };

    public static bool CanTransition(TransactionState from, TransactionState to)
        => Allowed.TryGetValue(from, out var next) && next.Contains(to);

    public static TransactionState Transition(TransactionState from, TransactionState to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Illegal transaction transition: {from} -> {to}.");
        }

        return to;
    }
}
