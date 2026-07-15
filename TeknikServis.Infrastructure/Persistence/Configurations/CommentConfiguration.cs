using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeknikServis.Application.Domain.Entities;

namespace TeknikServis.Infrastructure.Persistence.Configurations;

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("comments");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.AuthorType).IsRequired().HasMaxLength(20);
        builder.Property(c => c.Content).IsRequired().HasMaxLength(2000);
        builder.Property(c => c.CreatedAt).IsRequired();

        builder.HasIndex(c => new { c.ServiceTicketId, c.CreatedAt });

        builder.HasOne(c => c.ServiceTicket)
            .WithMany(t => t.Comments)
            .HasForeignKey(c => c.ServiceTicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
