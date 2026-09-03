using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Terminal.Core.Actions;
using Terminal.Core.Evidence;
using Terminal.Core.SystemState;

namespace Terminal.State;

public sealed class SqliteSystemGraphStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly SqliteOperationalStore _operationalStore;

    public SqliteSystemGraphStore(SqliteOperationalStore operationalStore)
    {
        _operationalStore = operationalStore ?? throw new ArgumentNullException(nameof(operationalStore));
    }

    public async ValueTask UpsertAsync(
        SystemFact fact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fact);
        await using var connection = await _operationalStore.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await BeginImmediateAsync(connection, cancellationToken).ConfigureAwait(false);
        try
        {
            var subjectJson = JsonSerializer.Serialize(fact.Subject, JsonOptions);
            var provenanceJson = JsonSerializer.Serialize(fact.Provenance, JsonOptions);
            var changed = await IsMaterialChangeAsync(
                connection,
                fact.Key,
                subjectJson,
                fact.Value,
                fact.Generation,
                cancellationToken).ConfigureAwait(false);
            if (changed)
            {
                await InvalidateDependentsAsync(connection, fact.Key, cancellationToken).ConfigureAwait(false);
            }

            var upsert = connection.CreateCommand();
            upsert.CommandText = """
                INSERT INTO system_facts(
                    fact_key, subject_json, fact_value, provenance_json,
                    observed_at_utc, max_age_ticks, generation, invalidated)
                VALUES (
                    $key, $subject, $value, $provenance,
                    $observedAt, $maxAgeTicks, $generation, 0)
                ON CONFLICT(fact_key) DO UPDATE SET
                    subject_json = excluded.subject_json,
                    fact_value = excluded.fact_value,
                    provenance_json = excluded.provenance_json,
                    observed_at_utc = excluded.observed_at_utc,
                    max_age_ticks = excluded.max_age_ticks,
                    generation = excluded.generation,
                    invalidated = 0;
                """;
            upsert.Parameters.AddWithValue("$key", fact.Key);
            upsert.Parameters.AddWithValue("$subject", subjectJson);
            upsert.Parameters.AddWithValue("$value", fact.Value);
            upsert.Parameters.AddWithValue("$provenance", provenanceJson);
            upsert.Parameters.AddWithValue("$observedAt", FormatUtc(fact.ObservedAt));
            upsert.Parameters.AddWithValue("$maxAgeTicks", fact.MaxAge.Ticks);
            upsert.Parameters.AddWithValue("$generation", fact.Generation);
            await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            var clearDependencies = connection.CreateCommand();
            clearDependencies.CommandText = "DELETE FROM system_fact_dependencies WHERE fact_key = $key;";
            clearDependencies.Parameters.AddWithValue("$key", fact.Key);
            await clearDependencies.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            foreach (var dependency in fact.Dependencies)
            {
                var insertDependency = connection.CreateCommand();
                insertDependency.CommandText = """
                    INSERT INTO system_fact_dependencies(fact_key, dependency_key)
                    VALUES ($key, $dependency);
                    """;
                insertDependency.Parameters.AddWithValue("$key", fact.Key);
                insertDependency.Parameters.AddWithValue("$dependency", dependency);
                await insertDependency.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await CommitAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await RollbackQuietlyAsync(connection, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask<bool> InvalidateAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Fact key must not be empty.", nameof(key));
        }

        await using var connection = await _operationalStore.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await BeginImmediateAsync(connection, cancellationToken).ConfigureAwait(false);
        try
        {
            var exists = connection.CreateCommand();
            exists.CommandText = "SELECT 1 FROM system_facts WHERE fact_key = $key LIMIT 1;";
            exists.Parameters.AddWithValue("$key", key);
            var found = await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
            if (found)
            {
                await InvalidateTreeAsync(connection, key, cancellationToken).ConfigureAwait(false);
            }

            await CommitAsync(connection, cancellationToken).ConfigureAwait(false);
            return found;
        }
        catch
        {
            await RollbackQuietlyAsync(connection, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask<SystemGraph> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _operationalStore.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var dependencies = await LoadDependenciesAsync(connection, cancellationToken).ConfigureAwait(false);
        var facts = new List<(SystemFact Fact, bool Invalidated)>();

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT fact_key, subject_json, fact_value, provenance_json,
                   observed_at_utc, max_age_ticks, generation, invalidated
            FROM system_facts
            ORDER BY fact_key COLLATE BINARY;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var key = reader.GetString(0);
            var subject = DeserializeRequired<ResourceRef>(reader.GetString(1), "ResourceRef");
            var provenance = DeserializeRequired<Provenance>(reader.GetString(3), "Provenance");
            facts.Add((
                new SystemFact(
                    key,
                    subject,
                    reader.GetString(2),
                    provenance,
                    ParseUtc(reader.GetString(4)),
                    TimeSpan.FromTicks(reader.GetInt64(5)),
                    reader.GetInt64(6),
                    dependencies.TryGetValue(key, out var deps) ? deps : []),
                reader.GetInt64(7) != 0));
        }

        var graph = new SystemGraph();
        foreach (var (fact, _) in facts)
        {
            graph.Upsert(fact);
        }

        foreach (var (fact, invalidated) in facts)
        {
            if (invalidated)
            {
                graph.Invalidate(fact.Key);
            }
        }

        return graph;
    }

    private static async Task<Dictionary<string, IReadOnlyList<string>>> LoadDependenciesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT fact_key, dependency_key
            FROM system_fact_dependencies
            ORDER BY fact_key COLLATE BINARY, dependency_key COLLATE BINARY;
            """;
        var mutable = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var key = reader.GetString(0);
            if (!mutable.TryGetValue(key, out var list))
            {
                list = [];
                mutable[key] = list;
            }
            list.Add(reader.GetString(1));
        }

        return mutable.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<string>)pair.Value.AsReadOnly(),
            StringComparer.Ordinal);
    }

    private static async Task<bool> IsMaterialChangeAsync(
        SqliteConnection connection,
        string key,
        string subjectJson,
        string value,
        long generation,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT subject_json, fact_value, generation
            FROM system_facts
            WHERE fact_key = $key;
            """;
        command.Parameters.AddWithValue("$key", key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        return !string.Equals(reader.GetString(0), subjectJson, StringComparison.Ordinal) ||
               !string.Equals(reader.GetString(1), value, StringComparison.Ordinal) ||
               reader.GetInt64(2) != generation;
    }

    private static Task InvalidateDependentsAsync(
        SqliteConnection connection,
        string key,
        CancellationToken cancellationToken)
        => ExecuteInvalidationAsync(connection, key, includeRoot: false, cancellationToken);

    private static Task InvalidateTreeAsync(
        SqliteConnection connection,
        string key,
        CancellationToken cancellationToken)
        => ExecuteInvalidationAsync(connection, key, includeRoot: true, cancellationToken);

    private static async Task ExecuteInvalidationAsync(
        SqliteConnection connection,
        string key,
        bool includeRoot,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = includeRoot
            ? """
                WITH RECURSIVE affected(fact_key) AS (
                    SELECT $key
                    UNION
                    SELECT d.fact_key
                    FROM system_fact_dependencies d
                    JOIN affected a ON d.dependency_key = a.fact_key
                )
                UPDATE system_facts
                SET invalidated = 1
                WHERE fact_key IN (SELECT fact_key FROM affected);
                """
            : """
                WITH RECURSIVE affected(fact_key) AS (
                    SELECT d.fact_key
                    FROM system_fact_dependencies d
                    WHERE d.dependency_key = $key
                    UNION
                    SELECT d.fact_key
                    FROM system_fact_dependencies d
                    JOIN affected a ON d.dependency_key = a.fact_key
                )
                UPDATE system_facts
                SET invalidated = 1
                WHERE fact_key IN (SELECT fact_key FROM affected);
                """;
        command.Parameters.AddWithValue("$key", key);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static T DeserializeRequired<T>(string json, string typeName)
        where T : class
        => JsonSerializer.Deserialize<T>(json, JsonOptions)
           ?? throw new InvalidDataException($"Stored {typeName} JSON was null or invalid.");

    private static string FormatUtc(DateTimeOffset value)
        => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtc(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

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
