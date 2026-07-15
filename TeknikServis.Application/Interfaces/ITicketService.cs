using TeknikServis.Application.Domain.Entities;
using TeknikServis.Application.DTOs;

namespace TeknikServis.Application.Interfaces;

public interface ITicketService
{
    Task<ServiceTicket> CreateAsync(CreateTicketRequest request, CancellationToken ct = default);
    Task<ServiceTicket> UpdateAsync(Guid ticketId, UpdateTicketRequest request, CancellationToken ct = default);
    Task<ServiceTicket> AssignTechnicianAsync(Guid ticketId, AssignTechnicianRequest request, CancellationToken ct = default);
    Task<ServiceTicket> ChangeStatusAsync(Guid ticketId, ChangeStatusRequest request, CancellationToken ct = default);
}
