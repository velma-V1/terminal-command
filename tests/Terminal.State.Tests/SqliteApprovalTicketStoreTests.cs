using Microsoft.Data.Sqlite;
using Terminal.Core.Authority;
using Terminal.State;

namespace Terminal.State.Tests;

public sealed class SqliteApprovalTicketStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-01T20:00:00Z");
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "terminal-v3-approval",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Persistent_store_allows_exactly_one_consumption()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var operationalStore = await CreateInitializedStoreAsync(cancellationToken);
        await using var disposeStore = operationalStore;

        var actionId = Guid.NewGuid();
        await SeedActionAsync(operationalStore, actionId, HashA, cancellationToken);

        IApprovalTicketStore store = new SqliteApprovalTicketStore(operationalStore);
        var ticket = ApprovalTicket.Issue(actionId, HashA, Now, TimeSpan.FromMinutes(5));
        await store.AddAsync(ticket, cancellationToken);

        var first = await store.ConsumeAsync(
            ticket.TicketId,
            actionId,
            HashA,
            Now.AddSeconds(1),
            cancellationToken);
        var second = await store.ConsumeAsync(
            ticket.TicketId,
            actionId,
            HashA,
            Now.AddSeconds(2),
            cancellationToken);

        Assert.Equal(ApprovalValidation.Valid, first.Validation);
        Assert.NotNull(first.Ticket?.ConsumedAt);
        Assert.Equal(ApprovalValidation.Consumed, second.Validation);
    }

    [Fact]
    public async Task Wrong_action_does_not_consume_persistent_ticket()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var operationalStore = await CreateInitializedStoreAsync(cancellationToken);
        await using var disposeStore = operationalStore;

        var actionId = Guid.NewGuid();
        await SeedActionAsync(operationalStore, actionId, HashA, cancellationToken);

        IApprovalTicketStore store = new SqliteApprovalTicketStore(operationalStore);
        var ticket = ApprovalTicket.Issue(actionId, HashA, Now, TimeSpan.FromMinutes(5));
        await store.AddAsync(ticket, cancellationToken);

        var wrong = await store.ConsumeAsync(
            ticket.TicketId,
            actionId,
            HashB,
            Now.AddSeconds(1),
            cancellationToken);
        var correct = await store.ConsumeAsync(
            ticket.TicketId,
            actionId,
            HashA,
            Now.AddSeconds(2),
            cancellationToken);

        Assert.Equal(ApprovalValidation.WrongAction, wrong.Validation);
        Assert.Equal(ApprovalValidation.Valid, correct.Validation);
    }

    [Fact]
    public async Task Concurrent_consumers_produce_exactly_one_authorization()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var operationalStore = await CreateInitializedStoreAsync(cancellationToken);
        await using var disposeStore = operationalStore;

        var actionId = Guid.NewGuid();
        await SeedActionAsync(operationalStore, actionId, HashA, cancellationToken);

        IApprovalTicketStore store = new SqliteApprovalTicketStore(operationalStore);
        var ticket = ApprovalTicket.Issue(actionId, HashA, Now, TimeSpan.FromMinutes(5));
        await store.AddAsync(ticket, cancellationToken);

        var consumers = Enumerable.Range(0, 8)
            .Select(index => store.ConsumeAsync(
                ticket.TicketId,
                actionId,
                HashA,
                Now.AddSeconds(index + 1),
                cancellationToken).AsTask())
            .ToArray();

        var results = await Task.WhenAll(consumers);

        Assert.Equal(1, results.Count(result => result.Validation == ApprovalValidation.Valid));
        Assert.Equal(7, results.Count(result => result.Validation == ApprovalValidation.Consumed));
    }

    [Fact]
    public async Task Ticket_survives_store_reopen_before_consumption()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "terminal.db");
        var actionId = Guid.NewGuid();
        var ticket = ApprovalTicket.Issue(actionId, HashA, Now, TimeSpan.FromMinutes(5));

        await using (var firstOperationalStore = new SqliteOperationalStore(path))
        {
            await firstOperationalStore.InitializeAsync(cancellationToken);
            await SeedActionAsync(firstOperationalStore, actionId, HashA, cancellationToken);
            IApprovalTicketStore firstStore = new SqliteApprovalTicketStore(firstOperationalStore);
            await firstStore.AddAsync(ticket, cancellationToken);
        }

        await using var secondOperationalStore = new SqliteOperationalStore(path);
        await secondOperationalStore.InitializeAsync(cancellationToken);
        IApprovalTicketStore secondStore = new SqliteApprovalTicketStore(secondOperationalStore);

        var result = await secondStore.ConsumeAsync(
            ticket.TicketId,
            actionId,
            HashA,
            Now.AddSeconds(1),
            cancellationToken);

        Assert.Equal(ApprovalValidation.Valid, result.Validation);
        Assert.NotNull(result.Ticket?.ConsumedAt);
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
        string actionHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await operationalStore.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO actions(action_id, action_hash, canonical_json, created_at_utc)
            VALUES ($actionId, $actionHash, '{}', $createdAtUtc);
            """;
        command.Parameters.AddWithValue("$actionId", actionId.ToString("D"));
        command.Parameters.AddWithValue("$actionHash", actionHash);
        command.Parameters.AddWithValue("$createdAtUtc", Now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
