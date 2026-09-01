namespace Terminal.Core.Transactions;

public enum TransactionState
{
    Prepared,
    Authorized,
    Started,
    SideEffectObserved,
    Verifying,
    Committed,
    Failed,
    Cancelled,
    Indeterminate,
    RollingBack,
    RolledBack,
    Compensating,
    Compensated
}
