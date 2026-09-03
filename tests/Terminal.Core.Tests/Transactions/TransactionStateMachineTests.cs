using Terminal.Core.Transactions;

namespace Terminal.Core.Tests.Transactions;

public sealed class TransactionStateMachineTests
{
    [Theory]
    [InlineData(TransactionState.Prepared, TransactionState.Authorized)]
    [InlineData(TransactionState.Authorized, TransactionState.Started)]
    [InlineData(TransactionState.Started, TransactionState.SideEffectObserved)]
    [InlineData(TransactionState.SideEffectObserved, TransactionState.Verifying)]
    [InlineData(TransactionState.Started, TransactionState.Verifying)]
    [InlineData(TransactionState.Verifying, TransactionState.Committed)]
    [InlineData(TransactionState.Started, TransactionState.Failed)]
    [InlineData(TransactionState.Started, TransactionState.Cancelled)]
    [InlineData(TransactionState.Started, TransactionState.Indeterminate)]
    [InlineData(TransactionState.Failed, TransactionState.RollingBack)]
    [InlineData(TransactionState.Cancelled, TransactionState.RollingBack)]
    [InlineData(TransactionState.Indeterminate, TransactionState.RollingBack)]
    [InlineData(TransactionState.RollingBack, TransactionState.RolledBack)]
    [InlineData(TransactionState.RollingBack, TransactionState.RollbackFailed)]
    [InlineData(TransactionState.Failed, TransactionState.Compensating)]
    [InlineData(TransactionState.Compensating, TransactionState.Compensated)]
    [InlineData(TransactionState.Compensating, TransactionState.CompensationFailed)]
    public void Legal_transition_is_allowed(TransactionState from, TransactionState to)
    {
        Assert.True(TransactionStateMachine.CanTransition(from, to));
        Assert.Equal(to, TransactionStateMachine.Transition(from, to));
    }

    [Theory]
    [InlineData(TransactionState.Prepared, TransactionState.Committed)]
    [InlineData(TransactionState.Committed, TransactionState.Started)]
    [InlineData(TransactionState.RolledBack, TransactionState.Committed)]
    [InlineData(TransactionState.Compensated, TransactionState.Started)]
    [InlineData(TransactionState.RollbackFailed, TransactionState.RollingBack)]
    [InlineData(TransactionState.CompensationFailed, TransactionState.Compensating)]
    [InlineData(TransactionState.Prepared, TransactionState.RolledBack)]
    public void Impossible_transition_is_rejected(TransactionState from, TransactionState to)
    {
        Assert.False(TransactionStateMachine.CanTransition(from, to));
        Assert.Throws<InvalidOperationException>(() => TransactionStateMachine.Transition(from, to));
    }

    [Theory]
    [InlineData(TransactionState.Committed)]
    [InlineData(TransactionState.RolledBack)]
    [InlineData(TransactionState.Compensated)]
    [InlineData(TransactionState.RollbackFailed)]
    [InlineData(TransactionState.CompensationFailed)]
    public void Terminal_state_has_no_outgoing_transition(TransactionState state)
    {
        foreach (var candidate in Enum.GetValues<TransactionState>())
        {
            Assert.False(TransactionStateMachine.CanTransition(state, candidate));
        }
    }
}
