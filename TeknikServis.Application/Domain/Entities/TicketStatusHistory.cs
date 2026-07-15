using TeknikServis.Application.Domain.Enums;

namespace TeknikServis.Application.Domain.Entities;

public class TicketStatusHistory
{
    public Guid Id { get; set; }

    public Guid ServiceTicketId { get; set; }
    public ServiceTicket ServiceTicket { get; set; } = null!;

    public TicketStatus? FromStatus { get; set; }
    public TicketStatus ToStatus { get; set; }

    public Guid? PreviousTechnicianId { get; set; }
    public Technician? PreviousTechnician { get; set; }

    public Guid? NewTechnicianId { get; set; }
    public Technician? NewTechnician { get; set; }

    public string ChangedByType { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime ChangedAt { get; set; }
}
