using TeknikServis.Application.DTOs;

namespace TeknikServis.Application.Interfaces;

public interface ITicketInteractionService
{
    Task<CommentResponse> AddCommentAsync(Guid ticketId, CreateCommentRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<CommentResponse>> GetCommentsAsync(Guid ticketId, CancellationToken ct = default);
    Task<AttachmentResponse> AddAttachmentAsync(Guid ticketId, CreateAttachmentRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AttachmentResponse>> GetAttachmentsAsync(Guid ticketId, CancellationToken ct = default);
}
