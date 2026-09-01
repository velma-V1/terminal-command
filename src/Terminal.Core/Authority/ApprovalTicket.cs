namespace Terminal.Core.Authority;

public enum ApprovalValidation
{
    Valid,
    WrongAction,
    Expired,
    Consumed,
    NotFound
}

public readonly record struct ApprovalTicketUseResult(
    ApprovalValidation Validation,
    ApprovalTicket? Ticket);

public sealed record ApprovalTicket
{
    private ApprovalTicket(
        Guid ticketId,
        Guid actionId,
        string actionHash,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        DateTimeOffset? consumedAt)
    {
        TicketId = ticketId;
        ActionId = actionId;
        ActionHash = actionHash;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        ConsumedAt = consumedAt;
    }

    public Guid TicketId { get; }
    public Guid ActionId { get; }
    public string ActionHash { get; }
    public DateTimeOffset IssuedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public DateTimeOffset? ConsumedAt { get; }

    public static ApprovalTicket Issue(
        Guid actionId,
        string actionHash,
        DateTimeOffset now,
        TimeSpan ttl)
    {
        if (actionId == Guid.Empty)
        {
            throw new ArgumentException("Action ID must not be empty.", nameof(actionId));
        }

        ValidateSha256(actionHash);

        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must be positive.");
        }

        return new ApprovalTicket(
            Guid.NewGuid(),
            actionId,
            actionHash,
            now,
            now.Add(ttl),
            consumedAt: null);
    }

    public ApprovalValidation Validate(Guid actionId, string actionHash, DateTimeOffset now)
    {
        if (ActionId != actionId || !string.Equals(ActionHash, actionHash, StringComparison.Ordinal))
        {
            return ApprovalValidation.WrongAction;
        }

        if (ConsumedAt is not null)
        {
            return ApprovalValidation.Consumed;
        }

        if (now >= ExpiresAt)
        {
            return ApprovalValidation.Expired;
        }

        return ApprovalValidation.Valid;
    }

    internal ApprovalTicket MarkConsumed(DateTimeOffset now)
        => new(TicketId, ActionId, ActionHash, IssuedAt, ExpiresAt, now);

    internal static ApprovalTicket Restore(
        Guid ticketId,
        Guid actionId,
        string actionHash,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        DateTimeOffset? consumedAt)
    {
        if (ticketId == Guid.Empty)
        {
            throw new InvalidDataException("Persisted approval ticket ID must not be empty.");
        }

        if (actionId == Guid.Empty)
        {
            throw new InvalidDataException("Persisted approval action ID must not be empty.");
        }

        ValidateSha256(actionHash);

        if (expiresAt <= issuedAt)
        {
            throw new InvalidDataException("Persisted approval ticket expiry must be after issuance.");
        }

        if (consumedAt is not null && consumedAt < issuedAt)
        {
            throw new InvalidDataException("Persisted approval ticket cannot be consumed before issuance.");
        }

        return new ApprovalTicket(ticketId, actionId, actionHash, issuedAt, expiresAt, consumedAt);
    }

    private static void ValidateSha256(string actionHash)
    {
        if (actionHash is null || actionHash.Length != 64 || actionHash.Any(static c => !IsLowerHex(c)))
        {
            throw new ArgumentException("Action hash must be a 64-character lower-case SHA-256 hex value.", nameof(actionHash));
        }
    }

    private static bool IsLowerHex(char value)
        => value is >= '0' and <= '9' or >= 'a' and <= 'f';
}
