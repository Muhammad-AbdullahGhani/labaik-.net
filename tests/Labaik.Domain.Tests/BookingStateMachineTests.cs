using Labaik.Domain.Bookings;
using Labaik.Domain.Pricing;
using Xunit;

namespace Labaik.Domain.Tests;

public sealed class BookingStateMachineTests
{
    public static IEnumerable<object[]> Cases() =>
        from fromState in Enum.GetValues<BookingState>() from to in Enum.GetValues<BookingState>()
        from role in Enum.GetValues<ActorRole>() select new object[] { fromState, to, role };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_state_pair_and_role_matches_the_contract(BookingState from, BookingState to, ActorRole role)
    {
        // Independent role-oriented contract, rather than reading the implementation's rule table.
        var edge = (from, to);
        var customer = edge is (BookingState.Draft, BookingState.PendingPayment) or
            (BookingState.Draft or BookingState.PendingPayment or BookingState.Paid or BookingState.Offered, BookingState.Cancelled) or
            (BookingState.InProgress or BookingState.ProofSubmitted or BookingState.Completed, BookingState.Disputed);
        var supervisor = edge is
            (BookingState.Draft or BookingState.PendingPayment or BookingState.Paid or BookingState.Offered or BookingState.Accepted, BookingState.Cancelled) or
            (BookingState.ProofSubmitted or BookingState.Disputed, BookingState.Completed) or
            (BookingState.InProgress or BookingState.ProofSubmitted or BookingState.Completed, BookingState.Disputed) or
            (BookingState.Disputed, BookingState.Cancelled);
        var expected = role switch
        {
            ActorRole.Customer => customer,
            ActorRole.Supervisor => supervisor,
            ActorRole.Admin => customer || supervisor,
            ActorRole.Performer => edge is (BookingState.Offered, BookingState.Accepted) or
                (BookingState.Accepted, BookingState.InProgress) or (BookingState.InProgress, BookingState.ProofSubmitted) or
                (BookingState.InProgress or BookingState.ProofSubmitted, BookingState.Disputed),
            ActorRole.System => edge is (BookingState.PendingPayment, BookingState.Paid or BookingState.Cancelled) or
                (BookingState.Paid, BookingState.Offered) or (BookingState.Offered, BookingState.Paid) or
                (BookingState.Cancelled, BookingState.Refunded),
            _ => false
        };
        var result = BookingStateMachine.Evaluate(from, to, role);
        Assert.Equal(expected, result.IsSuccess);
        if (expected) Assert.Null(result.Reason); else Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }
    [Fact]
    public void Invalid_enum_and_rejected_entity_transition_do_not_mutate_booking()
    {
        Assert.False(BookingStateMachine.Evaluate((BookingState)99, BookingState.Paid, ActorRole.System).IsSuccess);
        Assert.False(BookingStateMachine.Evaluate(BookingState.Draft, BookingState.Paid, (ActorRole)99).IsSuccess);
        var booking = new Booking(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 12, 1),
            new PriceQuote(new Money(100), new Money(0), new Money(0)), DateTimeOffset.UtcNow);
        Assert.False(booking.TryTransition(BookingState.Paid, ActorRole.Admin).IsSuccess);
        Assert.Equal(BookingState.Draft, booking.State);
        Assert.True(booking.TryTransition(BookingState.PendingPayment, ActorRole.Customer).IsSuccess);
        Assert.Equal(BookingState.PendingPayment, booking.State);
    }
}
