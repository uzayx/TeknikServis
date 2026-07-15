namespace TeknikServis.Application.Domain.Entities;

public class Attachment
{
    public Guid Id { get; set; }

    public Guid ServiceTicketId { get; set; }
    public ServiceTicket ServiceTicket { get; set; } = null!;

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string UploadedByType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
