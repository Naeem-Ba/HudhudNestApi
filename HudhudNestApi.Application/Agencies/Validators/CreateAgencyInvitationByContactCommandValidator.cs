using FluentValidation;
using HudhudNestApi.Application.Agencies.Commands.CreateAgencyInvitationByContact;

namespace HudhudNestApi.Application.Agencies.Validators;

/// <summary>Exactly one contact: an e-mail address or a phone number, never both or neither.</summary>
public sealed class CreateAgencyInvitationByContactCommandValidator
    : AbstractValidator<CreateAgencyInvitationByContactCommand>
{
    public CreateAgencyInvitationByContactCommandValidator()
    {
        RuleFor(x => x)
            .Must(x => string.IsNullOrWhiteSpace(x.Email) != string.IsNullOrWhiteSpace(x.PhoneNumber))
            .WithMessage("أدخل البريد الإلكتروني أو رقم الهاتف (واحدًا فقط).")
            .WithName("Contact");

        RuleFor(x => x.Email)
            .EmailAddress()
            .MaximumLength(256)
            .WithMessage("أدخل بريدًا إلكترونيًا صحيحًا.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(32)
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
    }
}
