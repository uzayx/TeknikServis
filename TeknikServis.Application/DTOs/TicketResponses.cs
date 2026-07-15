using TeknikServis.Application.Domain.Enums;

namespace TeknikServis.Application.DTOs;

public record TicketListItemResponse(
    Guid Id,
    string TicketNumber,
    string Title,
    TicketStatus Status,
    TicketPriority Priority,
    string CustomerName,
    string? TechnicianName,
    DateTime SlaDeadline,
    DateTime CreatedAt);

public record StatusHistoryResponse(
    Guid Id,
    TicketStatus? FromStatus,
    TicketStatus ToStatus,
    Guid? PreviousTechnicianId,
    Guid? NewTechnicianId,
    string ChangedByType,
    string? Note,
    DateTime ChangedAt);

public record TicketResponse(
    Guid Id,
    string TicketNumber,
    Guid CustomerId,
    string CustomerName,
    Guid? AssignedTechnicianId,
    string? TechnicianName,
    string Title,
    string Description,
    TicketStatus Status,
    TicketPriority Priority,
    DateTime SlaDeadline,
    DateTime CreatedAt,
    DateTime? AssignedAt,
    DateTime? CompletedAt,
    DateTime? ClosedAt,
    IReadOnlyList<TicketStatus> AllowedNextStatuses,
    IReadOnlyList<StatusHistoryResponse> StatusHistories);
