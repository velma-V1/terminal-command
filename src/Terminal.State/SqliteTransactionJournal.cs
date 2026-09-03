using Microsoft.Data.Sqlite;
using Terminal.Core.Transactions;

namespace Terminal.State;

public sealed class SqliteTransactionJournal : ITransactionJournal
{
    private static readonly HashSet<TransactionState> TerminalStates =
    [
        TransactionState.Committed,
        TransactionState.RolledBack,
        TransactionState.RollbackFailed,
        TransactionState.Compensated,
        TransactionState.CompensationFailed
    ];

    private readonly SqliteOperationalStore _operationalStore;

    public SqliteTransactionJournal(SqliteOperationalStore operationalStore)
    {
        _operationalStore = operationalStore ?? throw new ArgumentNullException(nameof(operationalStore));
    }

    public async ValueTask<TransactionRecord> CreateAsync(
        Guid transactionId,
        Guid actionId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (transactionId == Guid.Empty)
        {
            throw new ArgumentException("Transaction ID must not be empty.", nameof(transactionId));
        }

        if (actionId == Guid.Empty)
        {
            throw new ArgumentException("Action ID must not be empty.", nameof(actionId));
        }

        await using var connection = await _operationalStore
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await BeginImmediateAsync(connection, cancellationToken).ConfigureAwait(false);
        try
        {
            var timestamp = FormatUtc(now);
            var insertTransaction = connection.CreateCommand();
            insertTransaction.CommandText = """
                INSERT INTO transactions(
                    transaction_id,
                    action_id,
                    state,
                    created_at_utc,
                    updated_at_utc)
                VALUES (
                    $transactionId,
                    $actionId,
                    $state,
                    $createdAtUtc,
                    $updatedAtUtc);
                """;
            insertTransaction.Parameters.AddWithValue("$transactionId", transactionId.ToString("D"));
            insertTransaction.Parameters.AddWithValue("$actionId", actionId.ToString("D"));
            insertTransaction.Parameters.AddWithValue("$state", TransactionState.Prepared.ToString());
            insertTransaction.Parameters.AddWithValue("$createdAtUtc", timestamp);
            insertTransaction.Parameters.AddWithValue("$updatedAtUtc", timestamp);
            await insertTransaction.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await InsertEventAsync(
                connection,
                transactionId,
                fromState: null,
                TransactionState.Prepared,
                now,
                cancellationToken).ConfigureAwait(false);

            await CommitAsync(connection, cancellationToken).ConfigureAwait(false);
            return new TransactionRecord(
                transactionId,
                actionId,
                TransactionState.Prepared,
                now,
                now);
        }
        catch
        {
            await RollbackQuietlyAsync(connection, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask<TransactionRecord> TransitionAsync(
        Guid transactionId,
        TransactionState to,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (transactionId == Guid.Empty)
        {
            throw new ArgumentException("Transaction ID must not be empty.", nameof(transactionId));
        }

        await using var connection = await _operationalStore
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await BeginImmediateAsync(connection, cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await GetRequiredAsync(connection, transactionId, cancellationToken).ConfigureAwait(false);
            TransactionStateMachine.Transition(current.State, to);

            var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE transactions
                SET state = $toState,
                    updated_at_utc = $updatedAtUtc
                WHERE transaction_id = $transactionId
                  AND state = $fromState;
                """;
            update.Parameters.AddWithValue("$toState", to.ToString());
            update.Parameters.AddWithValue("$updatedAtUtc", FormatUtc(now));
            update.Parameters.AddWithValue("$transactionId", transactionId.ToString("D"));
            update.Parameters.AddWithValue("$fromState", current.State.ToString());

            var changed = await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (changed != 1)
            {
                throw new InvalidOperationException(
                    $"Transaction {transactionId} changed concurrently while transitioning {current.State} -> {to}.");
            }

            await InsertEventAsync(
                connection,
                transactionId,
                current.State,
                to,
                now,
                cancellationToken).ConfigureAwait(false);

            await CommitAsync(connection, cancellationToken).ConfigureAwait(false);
            return current with { State = to, UpdatedAt = now };
        }
        catch
        {
            await RollbackQuietlyAsync(connection, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask<TransactionRecord?> GetAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _operationalStore
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        return await GetOptionalAsync(connection, transactionId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<TransactionEventRecord>> ListEventsAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _operationalStore
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT event_id, transaction_id, from_state, to_state, occurred_at_utc
            FROM transaction_events
            WHERE transaction_id = $transactionId
            ORDER BY event_id ASC;
            """;
        command.Parameters.AddWithValue("$transactionId", transactionId.ToString("D"));

        var events = new List<TransactionEventRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            events.Add(ReadEvent(reader));
        }

        return events;
    }

    public async ValueTask<IReadOnlyList<TransactionRecord>> ListIncompleteAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _operationalStore
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT transaction_id, action_id, state, created_at_utc, updated_at_utc
            FROM transactions
            ORDER BY created_at_utc ASC, transaction_id ASC;
            """;

        var records = new List<TransactionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var record = ReadTransaction(reader);
            if (!TerminalStates.Contains(record.State))
            {
                records.Add(record);
            }
        }

        return records;
    }

    private static async Task<TransactionRecord> GetRequiredAsync(
        SqliteConnection connection,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        var record = await GetOptionalAsync(connection, transactionId, cancellationToken).ConfigureAwait(false);
        return record ?? throw new KeyNotFoundException($"Transaction {transactionId} was not found.");
    }

    private static async Task<TransactionRecord?> GetOptionalAsync(
        SqliteConnection connection,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT transaction_id, action_id, state, created_at_utc, updated_at_utc
            FROM transactions
            WHERE transaction_id = $transactionId;
            """;
        command.Parameters.AddWithValue("$transactionId", transactionId.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadTransaction(reader);
    }

    private static async Task InsertEventAsync(
        SqliteConnection connection,
        Guid transactionId,
        TransactionState? fromState,
        TransactionState toState,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO transaction_events(
                transaction_id,
                from_state,
                to_state,
                occurred_at_utc)
            VALUES (
                $transactionId,
                $fromState,
                $toState,
                $occurredAtUtc);
            """;
        command.Parameters.AddWithValue("$transactionId", transactionId.ToString("D"));
        command.Parameters.AddWithValue(
            "$fromState",
            fromState is null ? DBNull.Value : fromState.Value.ToString());
        command.Parameters.AddWithValue("$toState", toState.ToString());
        command.Parameters.AddWithValue("$occurredAtUtc", FormatUtc(occurredAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static TransactionRecord ReadTransaction(SqliteDataReader reader)
        => new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            ParseState(reader.GetString(2)),
            ParseTimestamp(reader.GetString(3)),
            ParseTimestamp(reader.GetString(4)));

    private static TransactionEventRecord ReadEvent(SqliteDataReader reader)
        => new(
            reader.GetInt64(0),
            Guid.Parse(reader.GetString(1)),
            reader.IsDBNull(2) ? null : ParseState(reader.GetString(2)),
            ParseState(reader.GetString(3)),
            ParseTimestamp(reader.GetString(4)));

    private static TransactionState ParseState(string value)
    {
        if (!Enum.TryParse<TransactionState>(value, ignoreCase: false, out var state) ||
            !Enum.IsDefined(state))
        {
            throw new InvalidDataException($"Unknown persisted transaction state '{value}'.");
        }

        return state;
    }

    private static DateTimeOffset ParseTimestamp(string value)
        => DateTimeOffset.Parse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);

    private static string FormatUtc(DateTimeOffset value)
        => value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static Task BeginImmediateAsync(SqliteConnection connection, CancellationToken cancellationToken)
        => ExecuteControlAsync(connection, "BEGIN IMMEDIATE;", cancellationToken);

    private static Task CommitAsync(SqliteConnection connection, CancellationToken cancellationToken)
        => ExecuteControlAsync(connection, "COMMIT;", cancellationToken);

    private static async Task RollbackQuietlyAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteControlAsync(connection, "ROLLBACK;", cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
            // Preserve the original exception when rollback itself cannot proceed.
        }
        catch (OperationCanceledException)
        {
            // Preserve the original exception when cancellation races rollback.
        }
    }

    private static async Task ExecuteControlAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
