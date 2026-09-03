using Microsoft.Data.Sqlite;
using Terminal.Core.Authority;

namespace Terminal.State;

public sealed class SqliteApprovalTicketStore : IApprovalTicketStore
{
    private readonly SqliteOperationalStore _operationalStore;

    public SqliteApprovalTicketStore(SqliteOperationalStore operationalStore)
    {
        _operationalStore = operationalStore ?? throw new ArgumentNullException(nameof(operationalStore));
    }

    public async ValueTask AddAsync(
        ApprovalTicket ticket,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        await using var connection = await _operationalStore
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO approval_tickets(
                ticket_id,
                action_id,
                action_hash,
                issued_at_utc,
                expires_at_utc,
                consumed_at_utc)
            VALUES (
                $ticketId,
                $actionId,
                $actionHash,
                $issuedAtUtc,
                $expiresAtUtc,
                $consumedAtUtc);
            """;
        command.Parameters.AddWithValue("$ticketId", ticket.TicketId.ToString("D"));
        command.Parameters.AddWithValue("$actionId", ticket.ActionId.ToString("D"));
        command.Parameters.AddWithValue("$actionHash", ticket.ActionHash);
        command.Parameters.AddWithValue("$issuedAtUtc", FormatUtc(ticket.IssuedAt));
        command.Parameters.AddWithValue("$expiresAtUtc", FormatUtc(ticket.ExpiresAt));
        command.Parameters.AddWithValue(
            "$consumedAtUtc",
            ticket.ConsumedAt is null ? DBNull.Value : FormatUtc(ticket.ConsumedAt.Value));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ApprovalTicketUseResult> ConsumeAsync(
        Guid ticketId,
        Guid actionId,
        string actionHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _operationalStore
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var nowUtc = FormatUtc(now);
        var consume = connection.CreateCommand();
        consume.CommandText = """
            UPDATE approval_tickets
            SET consumed_at_utc = $nowUtc
            WHERE ticket_id = $ticketId
              AND action_id = $actionId
              AND action_hash = $actionHash
              AND consumed_at_utc IS NULL
              AND expires_at_utc > $nowUtc
            RETURNING
                ticket_id,
                action_id,
                action_hash,
                issued_at_utc,
                expires_at_utc,
                consumed_at_utc;
            """;
        consume.Parameters.AddWithValue("$nowUtc", nowUtc);
        consume.Parameters.AddWithValue("$ticketId", ticketId.ToString("D"));
        consume.Parameters.AddWithValue("$actionId", actionId.ToString("D"));
        consume.Parameters.AddWithValue("$actionHash", actionHash);

        await using (var reader = await consume.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var consumed = ReadTicket(reader);
                return new ApprovalTicketUseResult(ApprovalValidation.Valid, consumed);
            }
        }

        var lookup = connection.CreateCommand();
        lookup.CommandText = """
            SELECT
                ticket_id,
                action_id,
                action_hash,
                issued_at_utc,
                expires_at_utc,
                consumed_at_utc
            FROM approval_tickets
            WHERE ticket_id = $ticketId;
            """;
        lookup.Parameters.AddWithValue("$ticketId", ticketId.ToString("D"));

        await using var lookupReader = await lookup.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await lookupReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new ApprovalTicketUseResult(ApprovalValidation.NotFound, null);
        }

        var existing = ReadTicket(lookupReader);
        return new ApprovalTicketUseResult(existing.Validate(actionId, actionHash, now), existing);
    }

    private static ApprovalTicket ReadTicket(SqliteDataReader reader)
    {
        var ticketId = Guid.Parse(reader.GetString(0));
        var actionId = Guid.Parse(reader.GetString(1));
        var actionHash = reader.GetString(2);
        var issuedAt = DateTimeOffset.Parse(
            reader.GetString(3),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
        var expiresAt = DateTimeOffset.Parse(
            reader.GetString(4),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
        DateTimeOffset? consumedAt = reader.IsDBNull(5)
            ? null
            : DateTimeOffset.Parse(
                reader.GetString(5),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind);

        return ApprovalTicket.Restore(
            ticketId,
            actionId,
            actionHash,
            issuedAt,
            expiresAt,
            consumedAt);
    }

    private static string FormatUtc(DateTimeOffset value)
        => value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
