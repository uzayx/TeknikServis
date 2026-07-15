using Microsoft.EntityFrameworkCore;
using TeknikServis.Application.Common.Exceptions;
using TeknikServis.Application.Domain;
using TeknikServis.Application.Domain.Entities;
using TeknikServis.Application.Domain.Enums;
using TeknikServis.Application.DTOs;
using TeknikServis.Application.Interfaces;

namespace TeknikServis.Application.Services;

public class TicketInteractionService : ITicketInteractionService
{
    private readonly IAppDbContext _db;

    public TicketInteractionService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<CommentResponse> AddCommentAsync(Guid ticketId, CreateCommentRequest request, CancellationToken ct = default)
    {
        var ticket = await GetTicketOrThrowAsync(ticketId, ct);

        // Yorumlar Closed haric her durumda eklenebilir: onay surecinde
        // musteri-merkez iletisimi devam edebilmeli.
        if (ticket.Status == TicketStatus.Closed)
            throw new BusinessRuleException(
                "TICKET_LOCKED",
                "Kapatilmis kayda yorum eklenemez.");

        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            ServiceTicketId = ticket.Id,
            AuthorType = request.AuthorType,
            AuthorId = request.AuthorId,
            Content = request.Content,
            CreatedAt = DateTime.UtcNow
        };

        _db.Comments.Add(comment);
        await _db.SaveChangesAsync(ct);

        return ToResponse(comment);
    }

    public async Task<IReadOnlyList<CommentResponse>> GetCommentsAsync(Guid ticketId, CancellationToken ct = default)
    {
        await EnsureTicketExistsAsync(ticketId, ct);

        return await _db.Comments
            .AsNoTracking()
            .Where(c => c.ServiceTicketId == ticketId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new CommentResponse(c.Id, c.ServiceTicketId, c.AuthorType, c.AuthorId, c.Content, c.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<AttachmentResponse> AddAttachmentAsync(Guid ticketId, CreateAttachmentRequest request, CancellationToken ct = default)
    {
        var ticket = await GetTicketOrThrowAsync(ticketId, ct);

        // Ekler (kanit/fotograf) onaydan ONCE gelmeli: Approved/Closed
        // kayitlara ek yuklemek denetim izini bulandirir.
        if (TicketStatusStateMachine.IsTerminalOrLocked(ticket.Status))
            throw new BusinessRuleException(
                "TICKET_LOCKED",
                $"'{ticket.Status}' durumundaki kayda ek yuklenemez.");

        var attachment = new Attachment
        {
            Id = Guid.NewGuid(),
            ServiceTicketId = ticket.Id,
            FileName = request.FileName,
            ContentType = request.ContentType,
            FileSizeBytes = request.FileSizeBytes,
            StoragePath = request.StoragePath,
            UploadedByType = request.UploadedByType,
            CreatedAt = DateTime.UtcNow
        };

        _db.Attachments.Add(attachment);
        await _db.SaveChangesAsync(ct);

        return ToResponse(attachment);
    }

    public async Task<IReadOnlyList<AttachmentResponse>> GetAttachmentsAsync(Guid ticketId, CancellationToken ct = default)
    {
        await EnsureTicketExistsAsync(ticketId, ct);

        return await _db.Attachments
            .AsNoTracking()
            .Where(a => a.ServiceTicketId == ticketId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new AttachmentResponse(
                a.Id, a.ServiceTicketId, a.FileName, a.ContentType,
                a.FileSizeBytes, a.StoragePath, a.UploadedByType, a.CreatedAt))
            .ToListAsync(ct);
    }

    private async Task<ServiceTicket> GetTicketOrThrowAsync(Guid ticketId, CancellationToken ct)
        => await _db.ServiceTickets.FirstOrDefaultAsync(t => t.Id == ticketId, ct)
           ?? throw new NotFoundException(nameof(ServiceTicket), ticketId);

    private async Task EnsureTicketExistsAsync(Guid ticketId, CancellationToken ct)
    {
        var exists = await _db.ServiceTickets.AnyAsync(t => t.Id == ticketId, ct);
        if (!exists)
            throw new NotFoundException(nameof(ServiceTicket), ticketId);
    }

    private static CommentResponse ToResponse(Comment c)
        => new(c.Id, c.ServiceTicketId, c.AuthorType, c.AuthorId, c.Content, c.CreatedAt);

    private static AttachmentResponse ToResponse(Attachment a)
        => new(a.Id, a.ServiceTicketId, a.FileName, a.ContentType,
               a.FileSizeBytes, a.StoragePath, a.UploadedByType, a.CreatedAt);
}
