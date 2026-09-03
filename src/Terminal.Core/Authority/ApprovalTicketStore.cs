using System.Collections.Concurrent;

namespace Terminal.Core.Authority;

public interface IApprovalTicketStore
{
    ValueTask AddAsync(
        ApprovalTicket ticket,
        CancellationToken cancellationToken = default);

    ValueTask<ApprovalTicketUseResult> ConsumeAsync(
        Guid ticketId,
        Guid actionId,
        string actionHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

public sealed class InMemoryApprovalTicketStore : IApprovalTicketStore
{
    private readonly ConcurrentDictionary<Guid, ApprovalTicket> _tickets = new();

    public ValueTask AddAsync(
        ApprovalTicket ticket,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(ticket);

        if (!_tickets.TryAdd(ticket.TicketId, ticket))
        {
            throw new InvalidOperationException($"Approval ticket {ticket.TicketId} already exists.");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<ApprovalTicketUseResult> ConsumeAsync(
        Guid ticketId,
        Guid actionId,
        string actionHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_tickets.TryGetValue(ticketId, out var current))
            {
                return ValueTask.FromResult(
                    new ApprovalTicketUseResult(ApprovalValidation.NotFound, null));
            }

            var validation = current.Validate(actionId, actionHash, now);
            if (validation != ApprovalValidation.Valid)
            {
                return ValueTask.FromResult(
                    new ApprovalTicketUseResult(validation, current));
            }

            var consumed = current.MarkConsumed(now);
            if (_tickets.TryUpdate(ticketId, consumed, current))
            {
                return ValueTask.FromResult(
                    new ApprovalTicketUseResult(ApprovalValidation.Valid, consumed));
            }
        }
    }
}
