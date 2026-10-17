using FluentValidation;

namespace HudhudNestApi.Application.Services.Commands.CreateServiceRequest;

public sealed class CreateServiceRequestCommandValidator : AbstractValidator<CreateServiceRequestCommand>
{
    public CreateServiceRequestCommandValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty();
        RuleFor(x => x.RequesterId).NotEmpty();
        RuleFor(x => x.ServiceOfferingId).NotEmpty();

        RuleFor(x => x.RequesterNote)
            .MaximumLength(1000)
            .When(x => x.RequesterNote is not null);

        // The first message of the conversation: required whenever the new flow is used.
        RuleFor(x => x.RequesterNote)
            .NotEmpty().WithMessage("اكتب رسالتك للمزوّد.")
            .When(x => x.Intent is not null);

        RuleFor(x => x.Intent!.Value).IsInEnum().When(x => x.Intent is not null);
    }
}
