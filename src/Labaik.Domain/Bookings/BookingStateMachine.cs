namespace Labaik.Domain.Bookings;

public enum BookingState { Draft, PendingPayment, Paid, Offered, Accepted, InProgress, ProofSubmitted, Completed, Cancelled, Refunded, Disputed }
public enum ActorRole { Customer, Performer, Supervisor, Accountant, Admin, System }

public static class BookingStateMachine
{
    // Admin can manage human workflows, but cannot assert a capture/refund or impersonate a performer.
    private static readonly Dictionary<(BookingState, BookingState), ActorRole[]> Rules = new()
    {
        [(BookingState.Draft, BookingState.PendingPayment)] = [ActorRole.Customer, ActorRole.Admin],
        [(BookingState.Draft, BookingState.Cancelled)] = [ActorRole.Customer, ActorRole.Supervisor, ActorRole.Admin],
        [(BookingState.PendingPayment, BookingState.Paid)] = [ActorRole.System],
        [(BookingState.PendingPayment, BookingState.Cancelled)] = [ActorRole.Customer, ActorRole.Supervisor, ActorRole.Admin, ActorRole.System],
        [(BookingState.Paid, BookingState.Offered)] = [ActorRole.System],
        [(BookingState.Paid, BookingState.Cancelled)] = [ActorRole.Customer, ActorRole.Supervisor, ActorRole.Admin],
        [(BookingState.Offered, BookingState.Paid)] = [ActorRole.System],
        [(BookingState.Offered, BookingState.Accepted)] = [ActorRole.Performer],
        [(BookingState.Offered, BookingState.Cancelled)] = [ActorRole.Customer, ActorRole.Supervisor, ActorRole.Admin],
        [(BookingState.Accepted, BookingState.InProgress)] = [ActorRole.Performer],
        [(BookingState.Accepted, BookingState.Cancelled)] = [ActorRole.Supervisor, ActorRole.Admin],
        [(BookingState.InProgress, BookingState.ProofSubmitted)] = [ActorRole.Performer],
        [(BookingState.InProgress, BookingState.Disputed)] = [ActorRole.Customer, ActorRole.Performer, ActorRole.Supervisor, ActorRole.Admin],
        [(BookingState.ProofSubmitted, BookingState.Completed)] = [ActorRole.Supervisor, ActorRole.Admin],
        [(BookingState.ProofSubmitted, BookingState.Disputed)] = [ActorRole.Customer, ActorRole.Performer, ActorRole.Supervisor, ActorRole.Admin],
        [(BookingState.Completed, BookingState.Disputed)] = [ActorRole.Customer, ActorRole.Supervisor, ActorRole.Admin],
        [(BookingState.Disputed, BookingState.Completed)] = [ActorRole.Supervisor, ActorRole.Admin],
        [(BookingState.Disputed, BookingState.Cancelled)] = [ActorRole.Supervisor, ActorRole.Admin],
        [(BookingState.Cancelled, BookingState.Refunded)] = [ActorRole.System]
    };

    public static RuleResult Evaluate(BookingState from, BookingState to, ActorRole role)
    {
        if (!Enum.IsDefined(from) || !Enum.IsDefined(to) || !Enum.IsDefined(role))
            return RuleResult.Deny("Unknown state or role.");
        if (!Rules.TryGetValue((from, to), out var roles))
            return RuleResult.Deny($"Transition {from} to {to} is not allowed.");
        return roles.Contains(role) ? RuleResult.Allow() : RuleResult.Deny($"Role {role} cannot perform {from} to {to}.");
    }
}
