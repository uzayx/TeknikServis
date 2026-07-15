namespace TeknikServis.Application.Domain.Entities;

public class Comment
{
    public Guid Id { get; set; }

    public Guid ServiceTicketId { get; set; }
    public ServiceTicket ServiceTicket { get; set; } = null!;

    public string AuthorType { get; set; } = string.Empty;
    public Guid? AuthorId { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
