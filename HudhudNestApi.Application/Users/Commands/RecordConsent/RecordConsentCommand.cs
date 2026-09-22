using FluentValidation;
using MediatR;
using HudhudNestApi.Application.Users.DTOs;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Application.Users.Commands.RecordConsent;

/// <summary>
/// Records that the authenticated caller explicitly agreed to one version of one policy
/// document, right now, from one client surface. The controller is the only caller —
/// there is no path that lets this run without the user having actually just performed
/// the consenting action (e.g. checking an unchecked box and submitting the form).
/// </summary>
public sealed record RecordConsentCommand(
    Guid UserId,
    ConsentPolicyType PolicyType,
    string PolicyVersion,
    ConsentSource Source)
    : IRequest<ConsentRecordDto>;

public sealed class RecordConsentCommandValidator : AbstractValidator<RecordConsentCommand>
{
    public RecordConsentCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.PolicyType).IsInEnum();
        RuleFor(x => x.Source).IsInEnum();
        RuleFor(x => x.PolicyVersion)
            .NotEmpty()
            .MaximumLength(64);
    }
}
