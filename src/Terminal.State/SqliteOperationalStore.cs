using Microsoft.Data.Sqlite;

namespace Terminal.State;

public sealed class SqliteOperationalStore : IAsyncDisposable
{
    private const int SchemaVersion = 2;
    private const int BusyTimeoutMilliseconds = 5_000;

    private readonly string _databasePath;
    private readonly string _connectionString;

    public SqliteOperationalStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path must not be empty.", nameof(databasePath));
        }

        _databasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SetAndVerifyWalAsync(connection, cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        await VerifySchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ConfigureConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<int> GetSchemaVersionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_info WHERE singleton = 1;";

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is null or DBNull)
        {
            throw new InvalidDataException("Operational database does not contain schema version metadata.");
        }

        return checked(Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture));
    }

    public async Task<IReadOnlyList<string>> ListUserTablesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_schema
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%'
            ORDER BY name COLLATE BINARY;
            """;

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"""
            PRAGMA foreign_keys = ON;
            PRAGMA synchronous = FULL;
            PRAGMA busy_timeout = {BusyTimeoutMilliseconds};
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SetAndVerifyWalAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var mode = Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture);

        if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"SQLite refused WAL journal mode; actual mode was '{mode ?? "<null>"}'.");
        }
    }

    private static async Task EnsureSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var metadata = connection.CreateCommand();
        metadata.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_info (
                singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
                version INTEGER NOT NULL CHECK (version > 0)
            ) STRICT;
            """;
        await metadata.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        var readVersion = connection.CreateCommand();
        readVersion.CommandText = "SELECT version FROM schema_info WHERE singleton = 1;";
        var rawVersion = await readVersion.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var existingVersion = rawVersion is null or DBNull
            ? 0
            : checked(Convert.ToInt32(rawVersion, System.Globalization.CultureInfo.InvariantCulture));

        if (existingVersion > SchemaVersion || existingVersion < 0)
        {
            throw new NotSupportedException(
                $"Unsupported Terminal operational schema version {existingVersion}; maximum supported is {SchemaVersion}.");
        }

        await BeginImmediateAsync(connection, cancellationToken).ConfigureAwait(false);
        try
        {
            await CreateCoreSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            await CreateLearningSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

            var writeVersion = connection.CreateCommand();
            writeVersion.CommandText = existingVersion == 0
                ? "INSERT INTO schema_info(singleton, version) VALUES (1, $version);"
                : "UPDATE schema_info SET version = $version WHERE singleton = 1;";
            writeVersion.Parameters.AddWithValue("$version", SchemaVersion);
            await writeVersion.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await CommitAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await RollbackQuietlyAsync(connection, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task CreateCoreSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS actions (
                action_id TEXT PRIMARY KEY NOT NULL,
                action_hash TEXT NOT NULL CHECK (length(action_hash) = 64),
                canonical_json TEXT NOT NULL,
                created_at_utc TEXT NOT NULL
            ) STRICT;

            CREATE TABLE IF NOT EXISTS approval_tickets (
                ticket_id TEXT PRIMARY KEY NOT NULL,
                action_id TEXT NOT NULL,
                action_hash TEXT NOT NULL CHECK (length(action_hash) = 64),
                issued_at_utc TEXT NOT NULL,
                expires_at_utc TEXT NOT NULL,
                consumed_at_utc TEXT,
                FOREIGN KEY (action_id) REFERENCES actions(action_id) ON DELETE CASCADE
            ) STRICT;

            CREATE TABLE IF NOT EXISTS transactions (
                transaction_id TEXT PRIMARY KEY NOT NULL,
                action_id TEXT NOT NULL,
                state TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                FOREIGN KEY (action_id) REFERENCES actions(action_id) ON DELETE RESTRICT
            ) STRICT;

            CREATE TABLE IF NOT EXISTS transaction_events (
                event_id INTEGER PRIMARY KEY AUTOINCREMENT,
                transaction_id TEXT NOT NULL,
                from_state TEXT,
                to_state TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                FOREIGN KEY (transaction_id) REFERENCES transactions(transaction_id) ON DELETE CASCADE
            ) STRICT;

            CREATE TABLE IF NOT EXISTS executions (
                execution_id TEXT PRIMARY KEY NOT NULL,
                transaction_id TEXT NOT NULL,
                backend TEXT NOT NULL,
                started_at_utc TEXT NOT NULL,
                ended_at_utc TEXT,
                exit_code INTEGER,
                status TEXT NOT NULL,
                FOREIGN KEY (transaction_id) REFERENCES transactions(transaction_id) ON DELETE CASCADE
            ) STRICT;

            CREATE TABLE IF NOT EXISTS verification_results (
                verification_id TEXT PRIMARY KEY NOT NULL,
                transaction_id TEXT NOT NULL,
                outcome TEXT NOT NULL,
                verifier_id TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                evidence_digest TEXT,
                FOREIGN KEY (transaction_id) REFERENCES transactions(transaction_id) ON DELETE CASCADE
            ) STRICT;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task CreateLearningSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS system_facts (
                fact_key TEXT PRIMARY KEY NOT NULL,
                subject_json TEXT NOT NULL,
                fact_value TEXT NOT NULL,
                provenance_json TEXT NOT NULL,
                observed_at_utc TEXT NOT NULL,
                max_age_ticks INTEGER NOT NULL CHECK (max_age_ticks > 0),
                generation INTEGER NOT NULL CHECK (generation > 0),
                invalidated INTEGER NOT NULL DEFAULT 0 CHECK (invalidated IN (0, 1))
            ) STRICT;

            CREATE TABLE IF NOT EXISTS system_fact_dependencies (
                fact_key TEXT NOT NULL,
                dependency_key TEXT NOT NULL,
                PRIMARY KEY (fact_key, dependency_key),
                FOREIGN KEY (fact_key) REFERENCES system_facts(fact_key) ON DELETE CASCADE
            ) STRICT;

            CREATE INDEX IF NOT EXISTS ix_system_fact_dependencies_dependency
                ON system_fact_dependencies(dependency_key);

            CREATE TABLE IF NOT EXISTS learned_knowledge (
                knowledge_id TEXT PRIMARY KEY NOT NULL,
                kind TEXT NOT NULL,
                trigger_signature TEXT NOT NULL,
                content TEXT NOT NULL,
                source_candidate_id TEXT NOT NULL,
                trust_class TEXT NOT NULL CHECK (trust_class = 'Verified'),
                promoted_at_utc TEXT NOT NULL
            ) STRICT;

            CREATE INDEX IF NOT EXISTS ix_learned_knowledge_trigger
                ON learned_knowledge(trigger_signature, promoted_at_utc);

            CREATE TABLE IF NOT EXISTS learned_knowledge_evidence (
                knowledge_id TEXT NOT NULL,
                ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
                source TEXT NOT NULL,
                source_class TEXT NOT NULL,
                PRIMARY KEY (knowledge_id, ordinal),
                FOREIGN KEY (knowledge_id) REFERENCES learned_knowledge(knowledge_id) ON DELETE CASCADE
            ) STRICT;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifySchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_info WHERE singleton = 1;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        if (result is null or DBNull)
        {
            throw new InvalidDataException("Operational database is missing schema metadata.");
        }

        var actualVersion = checked(Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture));
        if (actualVersion != SchemaVersion)
        {
            throw new NotSupportedException($"Unsupported Terminal operational schema version {actualVersion}; expected {SchemaVersion}.");
        }
    }

    private static async Task BeginImmediateAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "BEGIN IMMEDIATE;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task CommitAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "COMMIT;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task RollbackQuietlyAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            var command = connection.CreateCommand();
            command.CommandText = "ROLLBACK;";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
        }
    }
}
