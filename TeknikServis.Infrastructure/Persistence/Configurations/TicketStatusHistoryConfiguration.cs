using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeknikServis.Application.Domain.Entities;

namespace TeknikServis.Infrastructure.Persistence.Configurations;

public class TicketStatusHistoryConfiguration : IEntityTypeConfiguration<TicketStatusHistory>
{
    public void Configure(EntityTypeBuilder<TicketStatusHistory> builder)
    {
        builder.ToTable("ticket_status_histories");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.FromStatus).HasConversion<int>();
        builder.Property(h => h.ToStatus).IsRequired().HasConversion<int>();
        builder.Property(h => h.ChangedByType).IsRequired().HasMaxLength(20);
        builder.Property(h => h.Note).HasMaxLength(500);
        builder.Property(h => h.ChangedAt).IsRequired();

        builder.HasIndex(h => new { h.ServiceTicketId, h.ChangedAt });

        builder.HasOne(h => h.ServiceTicket)
            .WithMany(t => t.StatusHistories)
            .HasForeignKey(h => h.ServiceTicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(h => h.PreviousTechnician)
            .WithMany()
            .HasForeignKey(h => h.PreviousTechnicianId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.NewTechnician)
            .WithMany()
            .HasForeignKey(h => h.NewTechnicianId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
