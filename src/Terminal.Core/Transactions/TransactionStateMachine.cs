namespace Terminal.Core.Transactions;

public static class TransactionStateMachine
{
    private static readonly IReadOnlyDictionary<TransactionState, HashSet<TransactionState>> Allowed =
        new Dictionary<TransactionState, HashSet<TransactionState>>
        {
            [TransactionState.Prepared] = [TransactionState.Authorized, TransactionState.Failed, TransactionState.Cancelled],
            [TransactionState.Authorized] = [TransactionState.Started, TransactionState.Failed, TransactionState.Cancelled],
            [TransactionState.Started] = [TransactionState.SideEffectObserved, TransactionState.Verifying, TransactionState.Failed, TransactionState.Cancelled, TransactionState.Indeterminate],
            [TransactionState.SideEffectObserved] = [TransactionState.Verifying, TransactionState.Failed, TransactionState.Cancelled, TransactionState.Indeterminate],
            [TransactionState.Verifying] = [TransactionState.Committed, TransactionState.Failed, TransactionState.Indeterminate],
            [TransactionState.Failed] = [TransactionState.RollingBack, TransactionState.Compensating],
            [TransactionState.Cancelled] = [TransactionState.RollingBack, TransactionState.Compensating],
            [TransactionState.Indeterminate] = [TransactionState.RollingBack, TransactionState.Compensating],
            [TransactionState.RollingBack] = [TransactionState.RolledBack, TransactionState.Failed, TransactionState.Indeterminate],
            [TransactionState.Compensating] = [TransactionState.Compensated, TransactionState.Failed, TransactionState.Indeterminate],
            [TransactionState.Committed] = [],
            [TransactionState.RolledBack] = [],
            [TransactionState.Compensated] = []
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
