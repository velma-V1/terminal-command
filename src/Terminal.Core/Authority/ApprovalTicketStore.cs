using System.Collections.Concurrent;

namespace Terminal.Core.Authority;

public interface IApprovalTicketStore
{
    void Add(ApprovalTicket ticket);

    ApprovalTicketUseResult Consume(
        Guid ticketId,
        Guid actionId,
        string actionHash,
        DateTimeOffset now);
}

public sealed class InMemoryApprovalTicketStore : IApprovalTicketStore
{
    private readonly ConcurrentDictionary<Guid, ApprovalTicket> _tickets = new();

    public void Add(ApprovalTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (!_tickets.TryAdd(ticket.TicketId, ticket))
        {
            throw new InvalidOperationException($"Approval ticket {ticket.TicketId} already exists.");
        }
    }

    public ApprovalTicketUseResult Consume(
        Guid ticketId,
        Guid actionId,
        string actionHash,
        DateTimeOffset now)
    {
        while (true)
        {
            if (!_tickets.TryGetValue(ticketId, out var current))
            {
                return new ApprovalTicketUseResult(ApprovalValidation.NotFound, null);
            }

            var validation = current.Validate(actionId, actionHash, now);
            if (validation != ApprovalValidation.Valid)
            {
                return new ApprovalTicketUseResult(validation, current);
            }

            var consumed = current.MarkConsumed(now);
            if (_tickets.TryUpdate(ticketId, consumed, current))
            {
                return new ApprovalTicketUseResult(ApprovalValidation.Valid, consumed);
            }
        }
    }
}
