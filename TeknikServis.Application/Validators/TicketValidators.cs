using FluentValidation;
using TeknikServis.Application.DTOs;

namespace TeknikServis.Application.Validators;

public class CreateTicketRequestValidator : AbstractValidator<CreateTicketRequest>
{
    public CreateTicketRequestValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Priority).IsInEnum();
    }
}

public class UpdateTicketRequestValidator : AbstractValidator<UpdateTicketRequest>
{
    public UpdateTicketRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Priority).IsInEnum();
    }
}

public class AssignTechnicianRequestValidator : AbstractValidator<AssignTechnicianRequest>
{
    private static readonly string[] AllowedActors = { "Customer", "Technician", "Center" };

    public AssignTechnicianRequestValidator()
    {
        RuleFor(x => x.TechnicianId).NotEmpty();
        RuleFor(x => x.ChangedByType)
            .NotEmpty()
            .Must(v => AllowedActors.Contains(v))
            .WithMessage("ChangedByType su degerlerden biri olmali: Customer, Technician, Center.");
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public class ChangeStatusRequestValidator : AbstractValidator<ChangeStatusRequest>
{
    private static readonly string[] AllowedActors = { "Customer", "Technician", "Center" };

    public ChangeStatusRequestValidator()
    {
        RuleFor(x => x.NewStatus).IsInEnum();
        RuleFor(x => x.ChangedByType)
            .NotEmpty()
            .Must(v => AllowedActors.Contains(v))
            .WithMessage("ChangedByType su degerlerden biri olmali: Customer, Technician, Center.");
        RuleFor(x => x.Note).MaximumLength(500);
    }
}
