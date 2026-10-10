using FluentValidation;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Application.Users.Commands.UpdateUser;

public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.FirstName)
            .NotEmpty()
            .MaximumLength(100)
            .When(x => x.FirstName is not null);

        RuleFor(x => x.LastName)
            .NotEmpty()
            .MaximumLength(100)
            .When(x => x.LastName is not null);

        RuleFor(x => x.DisplayName)
            .MaximumLength(150)
            .When(x => x.DisplayName is not null);

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(30)
            .When(x => x.PhoneNumber is not null);

        RuleFor(x => x.ProfileImageUrl)
            .MaximumLength(2048)
            .When(x => x.ProfileImageUrl is not null);

        // Only codes the product actually translates / prices: anything else would be stored,
        // echoed back, and then fail (or silently fall back) in the notification localizer.
        RuleFor(x => x.PreferredLanguage)
            .Must(SupportedPreferences.IsSupportedLanguage)
            .WithMessage("Preferred language must be one of: " + string.Join(", ", SupportedPreferences.Languages) + ".")
            .When(x => x.PreferredLanguage is not null);

        RuleFor(x => x.PreferredCurrency)
            .Must(SupportedPreferences.IsSupportedCurrency)
            .WithMessage("Preferred currency must be one of: " + string.Join(", ", SupportedPreferences.Currencies) + ".")
            .When(x => x.PreferredCurrency is not null);

        RuleFor(x => x.CountryCode)
            .Length(2)
            .When(x => x.CountryCode is not null && x.CountryCode.Length > 0);

        RuleFor(x => x.Bio)
            .MaximumLength(2000)
            .When(x => x.Bio is not null);

        RuleFor(x => x.ContactInfo)
            .MaximumLength(500)
            .When(x => x.ContactInfo is not null);
    }
}

