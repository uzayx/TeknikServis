using TeknikServis.Application.Domain.Enums;

namespace TeknikServis.Application.DTOs;

public record CreateTicketRequest(
    Guid CustomerId,
    string Title,
    string Description,
    TicketPriority Priority);

public record UpdateTicketRequest(
    string Title,
    string Description,
    TicketPriority Priority);

public record AssignTechnicianRequest(
    Guid TechnicianId,
    string ChangedByType,
    string? Note);

public record ChangeStatusRequest(
    TicketStatus NewStatus,
    string ChangedByType,
    string? Note);
