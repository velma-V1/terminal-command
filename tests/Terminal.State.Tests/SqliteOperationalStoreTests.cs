using Microsoft.Data.Sqlite;
using Terminal.State;

namespace Terminal.State.Tests;

public sealed class SqliteOperationalStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "terminal-v3-state", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Initialize_is_idempotent_and_creates_schema_v1()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "terminal.db");
        await using var store = new SqliteOperationalStore(path);

        await store.InitializeAsync();
        await store.InitializeAsync();

        Assert.Equal(1, await store.GetSchemaVersionAsync());
        var tables = await store.ListUserTablesAsync();
        Assert.Contains("actions", tables);
        Assert.Contains("approval_tickets", tables);
        Assert.Contains("transactions", tables);
        Assert.Contains("transaction_events", tables);
        Assert.Contains("executions", tables);
        Assert.Contains("verification_results", tables);
    }

    [Fact]
    public async Task Every_operational_connection_enforces_safety_pragmas()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "terminal.db");
        await using var store = new SqliteOperationalStore(path);
        await store.InitializeAsync();

        await using var connection = await store.OpenConnectionAsync();

        Assert.Equal("wal", await ScalarTextAsync(connection, "PRAGMA journal_mode;"));
        Assert.Equal(2L, await ScalarInt64Async(connection, "PRAGMA synchronous;"));
        Assert.Equal(1L, await ScalarInt64Async(connection, "PRAGMA foreign_keys;"));
        Assert.True(await ScalarInt64Async(connection, "PRAGMA busy_timeout;") >= 5_000L);
    }

    [Fact]
    public async Task Foreign_keys_reject_orphan_transaction_events()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "terminal.db");
        await using var store = new SqliteOperationalStore(path);
        await store.InitializeAsync();
        await using var connection = await store.OpenConnectionAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO transaction_events(transaction_id, from_state, to_state, occurred_at_utc)
            VALUES ('missing', 'Prepared', 'Authorized', '2026-09-01T00:00:00.0000000+00:00');
            """;

        var error = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(19, error.SqliteErrorCode);
    }

    [Fact]
    public async Task Store_reopens_with_committed_schema_intact()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "terminal.db");

        await using (var first = new SqliteOperationalStore(path))
        {
            await first.InitializeAsync();
        }

        await using var second = new SqliteOperationalStore(path);
        await second.InitializeAsync();
        Assert.Equal(1, await second.GetSchemaVersionAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static async Task<string> ScalarTextAsync(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private static async Task<long> ScalarInt64Async(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
