namespace TeknikServis.Application.DTOs;

public record CreateCommentRequest(string AuthorType, Guid? AuthorId, string Content);

public record CommentResponse(
    Guid Id,
    Guid ServiceTicketId,
    string AuthorType,
    Guid? AuthorId,
    string Content,
    DateTime CreatedAt);

public record CreateAttachmentRequest(
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string StoragePath,
    string UploadedByType);

public record AttachmentResponse(
    Guid Id,
    Guid ServiceTicketId,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string StoragePath,
    string UploadedByType,
    DateTime CreatedAt);
