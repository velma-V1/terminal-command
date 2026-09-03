using System.Globalization;
using Microsoft.Data.Sqlite;
using Terminal.Core.Intelligence;
using Terminal.Core.Recovery;

namespace Terminal.State;

public sealed class SqliteLearningStore : IVerifiedKnowledgeStore
{
    private readonly SqliteOperationalStore _operationalStore;

    public SqliteLearningStore(SqliteOperationalStore operationalStore)
    {
        _operationalStore = operationalStore ?? throw new ArgumentNullException(nameof(operationalStore));
    }

    public async ValueTask SaveAsync(
        VerifiedKnowledgeRecord knowledge,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(knowledge);
        if (knowledge.KnowledgeId == Guid.Empty || knowledge.SourceCandidateId == Guid.Empty)
        {
            throw new ArgumentException("Knowledge and source candidate IDs must be non-empty.", nameof(knowledge));
        }

        if (knowledge.TrustClass != KnowledgeTrustClass.Verified)
        {
            throw new InvalidOperationException("Only verified knowledge may enter the durable learning store.");
        }

        if (string.IsNullOrWhiteSpace(knowledge.TriggerSignature) || string.IsNullOrWhiteSpace(knowledge.Content))
        {
            throw new ArgumentException("Knowledge trigger and content must be non-empty.", nameof(knowledge));
        }

        if (knowledge.Evidence.Count == 0)
        {
            throw new InvalidOperationException("Verified knowledge must retain its supporting evidence references.");
        }

        await using var connection = await _operationalStore.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await BeginImmediateAsync(connection, cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await GetByIdAsync(connection, knowledge.KnowledgeId, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                if (!Equivalent(existing, knowledge))
                {
                    throw new InvalidOperationException(
                        $"Knowledge ID {knowledge.KnowledgeId} already exists with different material content.");
                }

                await CommitAsync(connection, cancellationToken).ConfigureAwait(false);
                return;
            }

            var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO learned_knowledge(
                    knowledge_id, kind, trigger_signature, content,
                    source_candidate_id, trust_class, promoted_at_utc)
                VALUES (
                    $knowledgeId, $kind, $trigger, $content,
                    $sourceCandidateId, $trustClass, $promotedAt);
                """;
            insert.Parameters.AddWithValue("$knowledgeId", knowledge.KnowledgeId.ToString("D"));
            insert.Parameters.AddWithValue("$kind", knowledge.Kind.ToString());
            insert.Parameters.AddWithValue("$trigger", knowledge.TriggerSignature);
            insert.Parameters.AddWithValue("$content", knowledge.Content);
            insert.Parameters.AddWithValue("$sourceCandidateId", knowledge.SourceCandidateId.ToString("D"));
            insert.Parameters.AddWithValue("$trustClass", knowledge.TrustClass.ToString());
            insert.Parameters.AddWithValue("$promotedAt", FormatUtc(knowledge.PromotedAt));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            for (var index = 0; index < knowledge.Evidence.Count; index++)
            {
                var evidence = knowledge.Evidence[index];
                var insertEvidence = connection.CreateCommand();
                insertEvidence.CommandText = """
                    INSERT INTO learned_knowledge_evidence(
                        knowledge_id, ordinal, source, source_class)
                    VALUES ($knowledgeId, $ordinal, $source, $sourceClass);
                    """;
                insertEvidence.Parameters.AddWithValue("$knowledgeId", knowledge.KnowledgeId.ToString("D"));
                insertEvidence.Parameters.AddWithValue("$ordinal", index);
                insertEvidence.Parameters.AddWithValue("$source", evidence.Source);
                insertEvidence.Parameters.AddWithValue("$sourceClass", evidence.SourceClass);
                await insertEvidence.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await CommitAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await RollbackQuietlyAsync(connection, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask<IReadOnlyList<VerifiedKnowledgeRecord>> FindByTriggerAsync(
        string triggerSignature,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(triggerSignature))
        {
            throw new ArgumentException("Trigger signature must not be empty.", nameof(triggerSignature));
        }

        await using var connection = await _operationalStore.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT knowledge_id
            FROM learned_knowledge
            WHERE trigger_signature = $trigger
              AND trust_class = 'Verified'
            ORDER BY promoted_at_utc DESC, knowledge_id ASC;
            """;
        command.Parameters.AddWithValue("$trigger", triggerSignature);

        var ids = new List<Guid>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                ids.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        var records = new List<VerifiedKnowledgeRecord>(ids.Count);
        foreach (var id in ids)
        {
            var record = await GetByIdAsync(connection, id, cancellationToken).ConfigureAwait(false);
            if (record is not null)
            {
                records.Add(record);
            }
        }

        return records;
    }

    private static async Task<VerifiedKnowledgeRecord?> GetByIdAsync(
        SqliteConnection connection,
        Guid knowledgeId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT knowledge_id, kind, trigger_signature, content,
                   source_candidate_id, trust_class, promoted_at_utc
            FROM learned_knowledge
            WHERE knowledge_id = $knowledgeId;
            """;
        command.Parameters.AddWithValue("$knowledgeId", knowledgeId.ToString("D"));

        Guid id;
        KnowledgeKind kind;
        string trigger;
        string content;
        Guid sourceCandidateId;
        KnowledgeTrustClass trustClass;
        DateTimeOffset promotedAt;

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            id = Guid.Parse(reader.GetString(0));
            kind = Enum.Parse<KnowledgeKind>(reader.GetString(1), ignoreCase: false);
            trigger = reader.GetString(2);
            content = reader.GetString(3);
            sourceCandidateId = Guid.Parse(reader.GetString(4));
            trustClass = Enum.Parse<KnowledgeTrustClass>(reader.GetString(5), ignoreCase: false);
            promotedAt = ParseUtc(reader.GetString(6));
        }

        var evidenceCommand = connection.CreateCommand();
        evidenceCommand.CommandText = """
            SELECT source, source_class
            FROM learned_knowledge_evidence
            WHERE knowledge_id = $knowledgeId
            ORDER BY ordinal ASC;
            """;
        evidenceCommand.Parameters.AddWithValue("$knowledgeId", knowledgeId.ToString("D"));
        var evidence = new List<ModelEvidenceReference>();
        await using var evidenceReader = await evidenceCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await evidenceReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            evidence.Add(new ModelEvidenceReference(
                evidenceReader.GetString(0),
                evidenceReader.GetString(1)));
        }

        return new VerifiedKnowledgeRecord(
            id,
            kind,
            trigger,
            content,
            sourceCandidateId,
            trustClass,
            promotedAt,
            evidence.AsReadOnly());
    }

    private static bool Equivalent(VerifiedKnowledgeRecord left, VerifiedKnowledgeRecord right)
        => left.KnowledgeId == right.KnowledgeId &&
           left.Kind == right.Kind &&
           string.Equals(left.TriggerSignature, right.TriggerSignature, StringComparison.Ordinal) &&
           string.Equals(left.Content, right.Content, StringComparison.Ordinal) &&
           left.SourceCandidateId == right.SourceCandidateId &&
           left.TrustClass == right.TrustClass &&
           left.PromotedAt.Equals(right.PromotedAt) &&
           left.Evidence.SequenceEqual(right.Evidence);

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
