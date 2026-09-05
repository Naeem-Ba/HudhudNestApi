using FluentValidation;
using MediatR;
using PropertyApi.Domain.Enums;

namespace PropertyApi.Application.Users.Commands.WithdrawConsent;

/// <summary>
/// Withdraws every currently-active consent the caller has for one policy type (across
/// all versions), i.e. "I no longer consent to the Privacy Policy" rather than "I no
/// longer consent to exactly version 1.0 of it". Returns the number of records withdrawn
/// (0 is a valid, non-error outcome — the caller had nothing active to withdraw).
/// </summary>
public sealed record WithdrawConsentCommand(
    Guid UserId,
    ConsentPolicyType PolicyType)
    : IRequest<int>;

public sealed class WithdrawConsentCommandValidator : AbstractValidator<WithdrawConsentCommand>
{
    public WithdrawConsentCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.PolicyType).IsInEnum();
    }
}
