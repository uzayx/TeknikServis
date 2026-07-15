using TeknikServis.Application.Domain;
using TeknikServis.Application.Domain.Enums;
using Xunit;

namespace TeknikServis.Tests;

public class TicketStatusStateMachineTests
{
    [Theory]
    [InlineData(TicketStatus.New, TicketStatus.Assigned)]
    [InlineData(TicketStatus.Assigned, TicketStatus.InProgress)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Completed)]
    [InlineData(TicketStatus.Completed, TicketStatus.Approved)]
    [InlineData(TicketStatus.Approved, TicketStatus.Closed)]
    public void ValidForwardTransitions_ShouldBeAllowed(TicketStatus from, TicketStatus to)
    {
        Assert.True(TicketStatusStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(TicketStatus.Assigned, TicketStatus.New)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Assigned)]
    [InlineData(TicketStatus.Completed, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Approved, TicketStatus.Completed)]
    [InlineData(TicketStatus.Closed, TicketStatus.Approved)]
    public void BackwardTransitions_ShouldBeRejected(TicketStatus from, TicketStatus to)
    {
        Assert.False(TicketStatusStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(TicketStatus.New, TicketStatus.InProgress)]
    [InlineData(TicketStatus.New, TicketStatus.Closed)]
    [InlineData(TicketStatus.Assigned, TicketStatus.Completed)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Approved)]
    public void SkippingTransitions_ShouldBeRejected(TicketStatus from, TicketStatus to)
    {
        Assert.False(TicketStatusStateMachine.CanTransition(from, to));
    }

    [Fact]
    public void Closed_ShouldHaveNoAllowedTargets()
    {
        Assert.Empty(TicketStatusStateMachine.GetAllowedTargets(TicketStatus.Closed));
    }

    [Theory]
    [InlineData(TicketStatus.Approved)]
    [InlineData(TicketStatus.Closed)]
    public void ApprovedAndClosed_ShouldBeLocked(TicketStatus status)
    {
        Assert.True(TicketStatusStateMachine.IsTerminalOrLocked(status));
    }

    [Theory]
    [InlineData(TicketStatus.New)]
    [InlineData(TicketStatus.Assigned)]
    [InlineData(TicketStatus.InProgress)]
    [InlineData(TicketStatus.Completed)]
    public void EarlierStatuses_ShouldNotBeLocked(TicketStatus status)
    {
        Assert.False(TicketStatusStateMachine.IsTerminalOrLocked(status));
    }
}
