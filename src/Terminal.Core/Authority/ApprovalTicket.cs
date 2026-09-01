using System.Security.Cryptography;

namespace Terminal.Core.Authority;

public enum ApprovalValidation
{
    Valid,
    WrongAction,
    Expired,
    Consumed
}

public readonly record struct ApprovalTicketUseResult(
    ApprovalValidation Validation,
    ApprovalTicket Ticket);

public sealed record ApprovalTicket
{
    private ApprovalTicket(
        Guid ticketId,
        string actionHash,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        string nonceHash,
        DateTimeOffset? consumedAt)
    {
        TicketId = ticketId;
        ActionHash = actionHash;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        NonceHash = nonceHash;
        ConsumedAt = consumedAt;
    }

    public Guid TicketId { get; }
    public string ActionHash { get; }
    public DateTimeOffset IssuedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public string NonceHash { get; }
    public DateTimeOffset? ConsumedAt { get; }

    public static ApprovalTicket Issue(
        string actionHash,
        DateTimeOffset now,
        TimeSpan ttl,
        ReadOnlySpan<byte> nonce)
    {
        if (string.IsNullOrWhiteSpace(actionHash))
        {
            throw new ArgumentException("Action hash must not be empty.", nameof(actionHash));
        }

        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must be positive.");
        }

        if (nonce.IsEmpty)
        {
            throw new ArgumentException("Nonce must not be empty.", nameof(nonce));
        }

        var nonceDigest = SHA256.HashData(nonce);
        return new ApprovalTicket(
            Guid.NewGuid(),
            actionHash,
            now,
            now.Add(ttl),
            Convert.ToHexString(nonceDigest).ToLowerInvariant(),
            consumedAt: null);
    }

    public ApprovalValidation Validate(string actionHash, DateTimeOffset now)
    {
        if (ConsumedAt is not null)
        {
            return ApprovalValidation.Consumed;
        }

        if (!string.Equals(ActionHash, actionHash, StringComparison.Ordinal))
        {
            return ApprovalValidation.WrongAction;
        }

        if (now > ExpiresAt)
        {
            return ApprovalValidation.Expired;
        }

        return ApprovalValidation.Valid;
    }

    public ApprovalTicketUseResult Consume(string actionHash, DateTimeOffset now)
    {
        var validation = Validate(actionHash, now);
        if (validation != ApprovalValidation.Valid)
        {
            return new ApprovalTicketUseResult(validation, this);
        }

        var consumed = new ApprovalTicket(
            TicketId,
            ActionHash,
            IssuedAt,
            ExpiresAt,
            NonceHash,
            now);

        return new ApprovalTicketUseResult(ApprovalValidation.Valid, consumed);
    }
}
