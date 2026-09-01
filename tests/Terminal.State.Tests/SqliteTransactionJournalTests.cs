using Terminal.Core.Transactions;
using Terminal.State;

namespace Terminal.State.Tests;

public sealed class SqliteTransactionJournalTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-01T20:00:00Z");
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "terminal-v3-journal",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Create_and_transition_persist_state_and_event_history_atomically()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var operationalStore = await CreateInitializedStoreAsync(cancellationToken);
        await using var disposeStore = operationalStore;
        var actionId = Guid.NewGuid();
        await SeedActionAsync(operationalStore, actionId, cancellationToken);

        ITransactionJournal journal = new SqliteTransactionJournal(operationalStore);
        var transactionId = Guid.NewGuid();

        var created = await journal.CreateAsync(transactionId, actionId, Now, cancellationToken);
        var authorized = await journal.TransitionAsync(
            transactionId,
            TransactionState.Authorized,
            Now.AddSeconds(1),
            cancellationToken);
        var events = await journal.ListEventsAsync(transactionId, cancellationToken);

        Assert.Equal(TransactionState.Prepared, created.State);
        Assert.Equal(TransactionState.Authorized, authorized.State);
        Assert.Collection(
            events,
            first =>
            {
                Assert.Null(first.FromState);
                Assert.Equal(TransactionState.Prepared, first.ToState);
            },
            second =>
            {
                Assert.Equal(TransactionState.Prepared, second.FromState);
                Assert.Equal(TransactionState.Authorized, second.ToState);
            });
    }

    [Fact]
    public async Task Illegal_transition_changes_neither_current_state_nor_history()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var operationalStore = await CreateInitializedStoreAsync(cancellationToken);
        await using var disposeStore = operationalStore;
        var actionId = Guid.NewGuid();
        await SeedActionAsync(operationalStore, actionId, cancellationToken);

        ITransactionJournal journal = new SqliteTransactionJournal(operationalStore);
        var transactionId = Guid.NewGuid();
        await journal.CreateAsync(transactionId, actionId, Now, cancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => journal
            .TransitionAsync(
                transactionId,
                TransactionState.Committed,
                Now.AddSeconds(1),
                cancellationToken)
            .AsTask());

        var current = await journal.GetAsync(transactionId, cancellationToken);
        var events = await journal.ListEventsAsync(transactionId, cancellationToken);

        Assert.NotNull(current);
        Assert.Equal(TransactionState.Prepared, current.Value.State);
        Assert.Single(events);
    }

    [Fact]
    public async Task Reopen_preserves_current_state_and_complete_history()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "terminal.db");
        var actionId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();

        await using (var firstStore = new SqliteOperationalStore(path))
        {
            await firstStore.InitializeAsync(cancellationToken);
            await SeedActionAsync(firstStore, actionId, cancellationToken);
            ITransactionJournal firstJournal = new SqliteTransactionJournal(firstStore);
            await firstJournal.CreateAsync(transactionId, actionId, Now, cancellationToken);
            await firstJournal.TransitionAsync(
                transactionId,
                TransactionState.Authorized,
                Now.AddSeconds(1),
                cancellationToken);
            await firstJournal.TransitionAsync(
                transactionId,
                TransactionState.Started,
                Now.AddSeconds(2),
                cancellationToken);
        }

        await using var secondStore = new SqliteOperationalStore(path);
        await secondStore.InitializeAsync(cancellationToken);
        ITransactionJournal secondJournal = new SqliteTransactionJournal(secondStore);

        var current = await secondJournal.GetAsync(transactionId, cancellationToken);
        var events = await secondJournal.ListEventsAsync(transactionId, cancellationToken);

        Assert.NotNull(current);
        Assert.Equal(TransactionState.Started, current.Value.State);
        Assert.Equal(3, events.Count);
        Assert.Equal(TransactionState.Started, events[^1].ToState);
    }

    [Fact]
    public async Task List_incomplete_excludes_committed_transaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var operationalStore = await CreateInitializedStoreAsync(cancellationToken);
        await using var disposeStore = operationalStore;
        var actionId = Guid.NewGuid();
        await SeedActionAsync(operationalStore, actionId, cancellationToken);

        ITransactionJournal journal = new SqliteTransactionJournal(operationalStore);
        var committedId = Guid.NewGuid();
        var incompleteId = Guid.NewGuid();

        await journal.CreateAsync(committedId, actionId, Now, cancellationToken);
        await journal.TransitionAsync(committedId, TransactionState.Authorized, Now.AddSeconds(1), cancellationToken);
        await journal.TransitionAsync(committedId, TransactionState.Started, Now.AddSeconds(2), cancellationToken);
        await journal.TransitionAsync(committedId, TransactionState.Verifying, Now.AddSeconds(3), cancellationToken);
        await journal.TransitionAsync(committedId, TransactionState.Committed, Now.AddSeconds(4), cancellationToken);

        await journal.CreateAsync(incompleteId, actionId, Now.AddSeconds(5), cancellationToken);
        await journal.TransitionAsync(incompleteId, TransactionState.Authorized, Now.AddSeconds(6), cancellationToken);
        await journal.TransitionAsync(incompleteId, TransactionState.Started, Now.AddSeconds(7), cancellationToken);

        var incomplete = await journal.ListIncompleteAsync(cancellationToken);

        Assert.DoesNotContain(incomplete, item => item.TransactionId == committedId);
        Assert.Contains(incomplete, item => item.TransactionId == incompleteId && item.State == TransactionState.Started);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private async Task<SqliteOperationalStore> CreateInitializedStoreAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var store = new SqliteOperationalStore(Path.Combine(_directory, "terminal.db"));
        await store.InitializeAsync(cancellationToken);
        return store;
    }

    private static async Task SeedActionAsync(
        SqliteOperationalStore operationalStore,
        Guid actionId,
        CancellationToken cancellationToken)
    {
        await using var connection = await operationalStore.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO actions(action_id, action_hash, canonical_json, created_at_utc)
            VALUES ($actionId, $actionHash, '{}', $createdAtUtc);
            """;
        command.Parameters.AddWithValue("$actionId", actionId.ToString("D"));
        command.Parameters.AddWithValue("$actionHash", HashA);
        command.Parameters.AddWithValue("$createdAtUtc", Now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
