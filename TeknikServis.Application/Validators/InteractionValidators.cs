using FluentValidation;
using TeknikServis.Application.DTOs;

namespace TeknikServis.Application.Validators;

public class CreateCommentRequestValidator : AbstractValidator<CreateCommentRequest>
{
    private static readonly string[] AllowedActors = { "Customer", "Technician", "Center" };

    public CreateCommentRequestValidator()
    {
        RuleFor(x => x.AuthorType)
            .NotEmpty()
            .Must(v => AllowedActors.Contains(v))
            .WithMessage("AuthorType su degerlerden biri olmali: Customer, Technician, Center.");
        RuleFor(x => x.Content).NotEmpty().MaximumLength(2000);
    }
}

public class CreateAttachmentRequestValidator : AbstractValidator<CreateAttachmentRequest>
{
    private const long MaxFileSizeBytes = 25 * 1024 * 1024; // 25 MB
    private static readonly string[] AllowedUploaders = { "Customer", "Technician" };

    public CreateAttachmentRequestValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.FileSizeBytes)
            .GreaterThan(0)
            .LessThanOrEqualTo(MaxFileSizeBytes)
            .WithMessage("Dosya boyutu 25 MB'i asamaz.");
        RuleFor(x => x.StoragePath).NotEmpty().MaximumLength(500);
        RuleFor(x => x.UploadedByType)
            .NotEmpty()
            .Must(v => AllowedUploaders.Contains(v))
            .WithMessage("UploadedByType su degerlerden biri olmali: Customer, Technician.");
    }
}
