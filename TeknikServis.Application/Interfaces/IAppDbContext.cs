using Microsoft.EntityFrameworkCore;
using TeknikServis.Application.Domain.Entities;

namespace TeknikServis.Application.Interfaces;

public interface IAppDbContext
{
    DbSet<Customer> Customers { get; }
    DbSet<Technician> Technicians { get; }
    DbSet<ServiceTicket> ServiceTickets { get; }
    DbSet<TicketStatusHistory> TicketStatusHistories { get; }
    DbSet<Comment> Comments { get; }
    DbSet<Attachment> Attachments { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
