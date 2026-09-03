using Microsoft.Data.Sqlite;
using Terminal.State;

namespace Terminal.State.Tests;

public sealed class SqliteOperationalStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "terminal-v3-state", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Initialize_is_idempotent_and_creates_schema_v2()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "terminal.db");
        await using var store = new SqliteOperationalStore(path);

        await store.InitializeAsync(cancellationToken);
        await store.InitializeAsync(cancellationToken);

        Assert.Equal(2, await store.GetSchemaVersionAsync(cancellationToken));
        var tables = await store.ListUserTablesAsync(cancellationToken);
        Assert.Contains("actions", tables);
        Assert.Contains("approval_tickets", tables);
        Assert.Contains("transactions", tables);
        Assert.Contains("transaction_events", tables);
        Assert.Contains("executions", tables);
        Assert.Contains("verification_results", tables);
        Assert.Contains("system_facts", tables);
        Assert.Contains("system_fact_dependencies", tables);
        Assert.Contains("learned_knowledge", tables);
        Assert.Contains("learned_knowledge_evidence", tables);
    }

    [Fact]
    public async Task Existing_schema_v1_is_migrated_without_losing_operational_data()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "terminal-v1.db");

        await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await connection.OpenAsync(cancellationToken);
            var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE schema_info (
                    singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
                    version INTEGER NOT NULL CHECK (version > 0)
                ) STRICT;
                INSERT INTO schema_info(singleton, version) VALUES (1, 1);
                CREATE TABLE actions (
                    action_id TEXT PRIMARY KEY NOT NULL,
                    action_hash TEXT NOT NULL CHECK (length(action_hash) = 64),
                    canonical_json TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL
                ) STRICT;
                INSERT INTO actions(action_id, action_hash, canonical_json, created_at_utc)
                VALUES ('action-preserved', printf('%064d', 0), '{}', '2026-09-01T00:00:00+00:00');
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var store = new SqliteOperationalStore(path);
        await store.InitializeAsync(cancellationToken);

        Assert.Equal(2, await store.GetSchemaVersionAsync(cancellationToken));
        await using var migrated = await store.OpenConnectionAsync(cancellationToken);
        Assert.Equal(1L, await ScalarInt64Async(
            migrated,
            "SELECT COUNT(*) FROM actions WHERE action_id = 'action-preserved';",
            cancellationToken));
        var tables = await store.ListUserTablesAsync(cancellationToken);
        Assert.Contains("system_facts", tables);
        Assert.Contains("learned_knowledge", tables);
    }

    [Fact]
    public async Task Every_operational_connection_enforces_safety_pragmas()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "terminal.db");
        await using var store = new SqliteOperationalStore(path);
        await store.InitializeAsync(cancellationToken);

        await using var connection = await store.OpenConnectionAsync(cancellationToken);

        Assert.Equal("wal", await ScalarTextAsync(connection, "PRAGMA journal_mode;", cancellationToken));
        Assert.Equal(2L, await ScalarInt64Async(connection, "PRAGMA synchronous;", cancellationToken));
        Assert.Equal(1L, await ScalarInt64Async(connection, "PRAGMA foreign_keys;", cancellationToken));
        Assert.True(await ScalarInt64Async(connection, "PRAGMA busy_timeout;", cancellationToken) >= 5_000L);
    }

    [Fact]
    public async Task Foreign_keys_reject_orphan_transaction_events()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "terminal.db");
        await using var store = new SqliteOperationalStore(path);
        await store.InitializeAsync(cancellationToken);
        await using var connection = await store.OpenConnectionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO transaction_events(transaction_id, from_state, to_state, occurred_at_utc)
            VALUES ('missing', 'Prepared', 'Authorized', '2026-09-01T00:00:00.0000000+00:00');
            """;

        var error = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync(cancellationToken));
        Assert.Equal(19, error.SqliteErrorCode);
    }

    [Fact]
    public async Task Store_reopens_with_committed_schema_intact()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "terminal.db");

        await using (var first = new SqliteOperationalStore(path))
        {
            await first.InitializeAsync(cancellationToken);
        }

        await using var second = new SqliteOperationalStore(path);
        await second.InitializeAsync(cancellationToken);
        Assert.Equal(2, await second.GetSchemaVersionAsync(cancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static async Task<string> ScalarTextAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private static async Task<long> ScalarInt64Async(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
