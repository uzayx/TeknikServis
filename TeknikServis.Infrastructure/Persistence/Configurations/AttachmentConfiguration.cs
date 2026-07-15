using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeknikServis.Application.Domain.Entities;

namespace TeknikServis.Infrastructure.Persistence.Configurations;

public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("attachments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.FileName).IsRequired().HasMaxLength(255);
        builder.Property(a => a.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(a => a.FileSizeBytes).IsRequired();
        builder.Property(a => a.StoragePath).IsRequired().HasMaxLength(500);
        builder.Property(a => a.UploadedByType).IsRequired().HasMaxLength(20);
        builder.Property(a => a.CreatedAt).IsRequired();

        builder.HasIndex(a => a.ServiceTicketId);

        builder.HasOne(a => a.ServiceTicket)
            .WithMany(t => t.Attachments)
            .HasForeignKey(a => a.ServiceTicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
