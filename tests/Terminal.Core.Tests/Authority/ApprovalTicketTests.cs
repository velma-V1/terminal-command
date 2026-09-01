using Terminal.Core.Authority;

namespace Terminal.Core.Tests.Authority;

public sealed class ApprovalTicketTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-01T20:00:00Z");
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void Ticket_accepts_only_bound_action_hash()
    {
        var ticket = ApprovalTicket.Issue(HashA, Now, TimeSpan.FromMinutes(5), [1, 2, 3, 4]);

        Assert.Equal(ApprovalValidation.Valid, ticket.Validate(HashA, Now.AddMinutes(1)));
        Assert.Equal(ApprovalValidation.WrongAction, ticket.Validate(HashB, Now.AddMinutes(1)));
    }

    [Fact]
    public void Expired_ticket_is_rejected()
    {
        var ticket = ApprovalTicket.Issue(HashA, Now, TimeSpan.FromSeconds(30), [1, 2, 3, 4]);

        Assert.Equal(ApprovalValidation.Expired, ticket.Validate(HashA, Now.AddSeconds(31)));
    }

    [Fact]
    public void Consuming_ticket_returns_consumed_copy_and_prevents_reuse()
    {
        var ticket = ApprovalTicket.Issue(HashA, Now, TimeSpan.FromMinutes(5), [1, 2, 3, 4]);

        var first = ticket.Consume(HashA, Now.AddSeconds(1));
        var second = first.Ticket.Consume(HashA, Now.AddSeconds(2));

        Assert.Equal(ApprovalValidation.Valid, first.Validation);
        Assert.NotNull(first.Ticket.ConsumedAt);
        Assert.Equal(ApprovalValidation.Consumed, first.Ticket.Validate(HashA, Now.AddSeconds(2)));
        Assert.Equal(ApprovalValidation.Consumed, second.Validation);
    }

    [Fact]
    public void Failed_consumption_does_not_consume_ticket()
    {
        var ticket = ApprovalTicket.Issue(HashA, Now, TimeSpan.FromMinutes(5), [1, 2, 3, 4]);

        var wrong = ticket.Consume(HashB, Now.AddSeconds(1));

        Assert.Equal(ApprovalValidation.WrongAction, wrong.Validation);
        Assert.Null(wrong.Ticket.ConsumedAt);
        Assert.Equal(ApprovalValidation.Valid, wrong.Ticket.Validate(HashA, Now.AddSeconds(2)));
    }

    [Fact]
    public void Separate_issues_have_distinct_ticket_identity_and_nonce_hash()
    {
        var first = ApprovalTicket.Issue(HashA, Now, TimeSpan.FromMinutes(5), [1, 2, 3, 4]);
        var second = ApprovalTicket.Issue(HashA, Now, TimeSpan.FromMinutes(5), [5, 6, 7, 8]);

        Assert.NotEqual(first.TicketId, second.TicketId);
        Assert.NotEqual(first.NonceHash, second.NonceHash);
    }
}
