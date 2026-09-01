using Terminal.Core.Authority;

namespace Terminal.Core.Tests.Authority;

public sealed class ApprovalTicketTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-01T20:00:00Z");
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void Ticket_binds_both_action_id_and_semantic_hash()
    {
        var actionId = Guid.NewGuid();
        var ticket = ApprovalTicket.Issue(actionId, HashA, Now, TimeSpan.FromMinutes(5));

        Assert.Equal(ApprovalValidation.Valid, ticket.Validate(actionId, HashA, Now.AddMinutes(1)));
        Assert.Equal(ApprovalValidation.WrongAction, ticket.Validate(Guid.NewGuid(), HashA, Now.AddMinutes(1)));
        Assert.Equal(ApprovalValidation.WrongAction, ticket.Validate(actionId, HashB, Now.AddMinutes(1)));
    }

    [Fact]
    public void Ticket_is_expired_at_expiry_boundary()
    {
        var actionId = Guid.NewGuid();
        var ticket = ApprovalTicket.Issue(actionId, HashA, Now, TimeSpan.FromSeconds(30));

        Assert.Equal(ApprovalValidation.Expired, ticket.Validate(actionId, HashA, Now.AddSeconds(30)));
    }

    [Fact]
    public void Store_allows_exactly_one_successful_consumption()
    {
        var actionId = Guid.NewGuid();
        var ticket = ApprovalTicket.Issue(actionId, HashA, Now, TimeSpan.FromMinutes(5));
        var store = new InMemoryApprovalTicketStore();
        store.Add(ticket);

        var first = store.Consume(ticket.TicketId, actionId, HashA, Now.AddSeconds(1));
        var second = store.Consume(ticket.TicketId, actionId, HashA, Now.AddSeconds(2));

        Assert.Equal(ApprovalValidation.Valid, first.Validation);
        Assert.NotNull(first.Ticket?.ConsumedAt);
        Assert.Equal(ApprovalValidation.Consumed, second.Validation);
    }

    [Fact]
    public void Failed_consumption_does_not_consume_ticket()
    {
        var actionId = Guid.NewGuid();
        var ticket = ApprovalTicket.Issue(actionId, HashA, Now, TimeSpan.FromMinutes(5));
        var store = new InMemoryApprovalTicketStore();
        store.Add(ticket);

        var wrong = store.Consume(ticket.TicketId, Guid.NewGuid(), HashA, Now.AddSeconds(1));
        var correct = store.Consume(ticket.TicketId, actionId, HashA, Now.AddSeconds(2));

        Assert.Equal(ApprovalValidation.WrongAction, wrong.Validation);
        Assert.Equal(ApprovalValidation.Valid, correct.Validation);
    }

    [Fact]
    public void Separate_issues_have_distinct_ticket_identity()
    {
        var actionId = Guid.NewGuid();
        var first = ApprovalTicket.Issue(actionId, HashA, Now, TimeSpan.FromMinutes(5));
        var second = ApprovalTicket.Issue(actionId, HashA, Now, TimeSpan.FromMinutes(5));

        Assert.NotEqual(first.TicketId, second.TicketId);
    }

    [Fact]
    public void Missing_ticket_is_not_authorized()
    {
        var store = new InMemoryApprovalTicketStore();
        var result = store.Consume(Guid.NewGuid(), Guid.NewGuid(), HashA, Now);
        Assert.Equal(ApprovalValidation.NotFound, result.Validation);
    }
}
