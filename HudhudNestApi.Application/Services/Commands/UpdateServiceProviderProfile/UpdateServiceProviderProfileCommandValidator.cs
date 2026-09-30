using FluentValidation;

namespace HudhudNestApi.Application.Services.Commands.UpdateServiceProviderProfile;

public sealed class UpdateServiceProviderProfileCommandValidator
    : AbstractValidator<UpdateServiceProviderProfileCommand>
{
    public UpdateServiceProviderProfileCommandValidator()
    {
        RuleFor(x => x.ActorUserId).NotEmpty();

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("اسم مزوّد الخدمة مطلوب.")
            .MaximumLength(150);

        RuleFor(x => x.Bio)
            .MaximumLength(2000)
            .When(x => x.Bio is not null);

        RuleFor(x => x.ContactEmail)
            .EmailAddress().WithMessage("صيغة البريد الإلكتروني غير صحيحة.")
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));

        RuleFor(x => x.ContactPhone)
            .Matches(@"^\+?[0-9\s\-]{7,20}$").WithMessage("صيغة رقم الهاتف غير صحيحة.")
            .When(x => !string.IsNullOrWhiteSpace(x.ContactPhone));
    }
}
