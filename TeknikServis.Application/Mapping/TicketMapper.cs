using TeknikServis.Application.Domain;
using TeknikServis.Application.Domain.Entities;
using TeknikServis.Application.DTOs;

namespace TeknikServis.Application.Mapping;

public static class TicketMapper
{
    public static TicketResponse ToResponse(ServiceTicket t) => new(
        t.Id,
        t.TicketNumber,
        t.CustomerId,
        t.Customer?.FullName ?? string.Empty,
        t.AssignedTechnicianId,
        t.AssignedTechnician?.FullName,
        t.Title,
        t.Description,
        t.Status,
        t.Priority,
        t.SlaDeadline,
        t.CreatedAt,
        t.AssignedAt,
        t.CompletedAt,
        t.ClosedAt,
        TicketStatusStateMachine.GetAllowedTargets(t.Status),
        t.StatusHistories
            .OrderBy(h => h.ChangedAt)
            .Select(h => new StatusHistoryResponse(
                h.Id, h.FromStatus, h.ToStatus,
                h.PreviousTechnicianId, h.NewTechnicianId,
                h.ChangedByType, h.Note, h.ChangedAt))
            .ToList());
}
