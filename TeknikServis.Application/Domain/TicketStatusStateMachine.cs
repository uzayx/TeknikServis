using TeknikServis.Application.Domain.Enums;

namespace TeknikServis.Application.Domain;

public static class TicketStatusStateMachine
{
    private static readonly IReadOnlyDictionary<TicketStatus, TicketStatus[]> AllowedTransitions =
        new Dictionary<TicketStatus, TicketStatus[]>
        {
            [TicketStatus.New] = new[] { TicketStatus.Assigned },
            [TicketStatus.Assigned] = new[] { TicketStatus.InProgress },
            [TicketStatus.InProgress] = new[] { TicketStatus.Completed },
            [TicketStatus.Completed] = new[] { TicketStatus.Approved },
            [TicketStatus.Approved] = new[] { TicketStatus.Closed },
            [TicketStatus.Closed] = Array.Empty<TicketStatus>()
        };

    public static bool CanTransition(TicketStatus from, TicketStatus to)
        => AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);

    public static bool IsTerminalOrLocked(TicketStatus status)
        => status is TicketStatus.Approved or TicketStatus.Closed;

    public static IReadOnlyList<TicketStatus> GetAllowedTargets(TicketStatus from)
        => AllowedTransitions.TryGetValue(from, out var targets) ? targets : Array.Empty<TicketStatus>();
}
