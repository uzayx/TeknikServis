using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeknikServis.Application.Domain.Entities;

namespace TeknikServis.Infrastructure.Persistence.Configurations;

public class ServiceTicketConfiguration : IEntityTypeConfiguration<ServiceTicket>
{
    public void Configure(EntityTypeBuilder<ServiceTicket> builder)
    {
        builder.ToTable("service_tickets");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TicketNumber).IsRequired().HasMaxLength(20);
        builder.Property(t => t.Title).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Description).IsRequired().HasMaxLength(2000);

        builder.Property(t => t.Status).IsRequired().HasConversion<int>();
        builder.Property(t => t.Priority).IsRequired().HasConversion<int>();

        builder.Property(t => t.SlaDeadline).IsRequired();
        builder.Property(t => t.CreatedAt).IsRequired();

        builder.HasIndex(t => t.TicketNumber).IsUnique();
        // KALDIRILDI: Tekil Status index'i -- asagidaki composite'in onegi.
        // Olcum: composite idx_scan = 3, tekil = 0. Planlayici hep composite'i seciyor.
        builder.HasIndex(t => t.CreatedAt);
        builder.HasIndex(t => new { t.Status, t.AssignedTechnicianId });

        builder.HasOne(t => t.Customer)
            .WithMany(c => c.ServiceTickets)
            .HasForeignKey(t => t.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.AssignedTechnician)
            .WithMany(tech => tech.AssignedTickets)
            .HasForeignKey(t => t.AssignedTechnicianId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}


