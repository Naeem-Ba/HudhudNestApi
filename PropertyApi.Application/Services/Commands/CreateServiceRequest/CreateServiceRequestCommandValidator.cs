using FluentValidation;

namespace PropertyApi.Application.Services.Commands.CreateServiceRequest;

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
    }
}
