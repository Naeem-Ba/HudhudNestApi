using FluentValidation;

namespace PropertyApi.Application.Users.Commands.RegisterUser;

public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    private static readonly HashSet<string> SupportedLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "en", "de", "ar"
    };

    private static readonly HashSet<string> SupportedCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "EUR", "USD", "GBP", "SYP", "TRY", "AED", "SAR"
    };

    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.LastName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.DisplayName)
            .MaximumLength(150)
            .When(x => !string.IsNullOrWhiteSpace(x.DisplayName));

        RuleFor(x => x.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Email is required.")
            .MaximumLength(254)
            .WithMessage("Email must not exceed 254 characters.")
            .EmailAddress()
            .WithMessage("Email must be a valid email address.")
            .Must(email => !email.Contains(' '))
            .WithMessage("Email must be a valid email address.");

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.");

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(30)
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));

        RuleFor(x => x.PreferredLanguage)
            .NotEmpty()
            .MaximumLength(10)
            .Must(lang => SupportedLanguages.Contains(lang))
            .WithMessage("Unsupported preferred language.");

        RuleFor(x => x.PreferredCurrency)
            .NotEmpty()
            .Length(3)
            .Must(currency => SupportedCurrencies.Contains(currency))
            .WithMessage("Unsupported preferred currency.");

        RuleFor(x => x.CountryCode)
            .Length(2)
            .When(x => !string.IsNullOrWhiteSpace(x.CountryCode));
    }
}

