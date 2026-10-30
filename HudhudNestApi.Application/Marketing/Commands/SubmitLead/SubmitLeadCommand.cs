using FluentValidation;
using MediatR;
using System.Text.RegularExpressions;
using HudhudNestApi.Application.Marketing.DTOs;

namespace HudhudNestApi.Application.Marketing.Commands.SubmitLead;

public sealed record SubmitLeadCommand(
    string FullName,
    string Phone,
    string City,
    string UserType,
    string Source,
    string? Notes,
    string? Campaign,
    Guid? OfferId,
    string? IpAddress) : IRequest<SubmitLeadResultDto>;

public sealed partial class SubmitLeadCommandValidator : AbstractValidator<SubmitLeadCommand>
{
    public SubmitLeadCommandValidator()
    {
        // Same rule as the landing form's phoneValidator: separators are ignored, then 7 to 15 digits
        // with an optional + or 00 prefix. The length-only check let "abcdefgh" through (QA Phase 12, P12-D10).
        RuleFor(x => x.Phone)
            .Must(p => p is not null && PhonePattern().IsMatch(SeparatorPattern().Replace(p, "")))
            .WithMessage("صيغة رقم الهاتف غير صحيحة.");
    }

    [GeneratedRegex(@"[\s\-().]")]
    private static partial Regex SeparatorPattern();

    [GeneratedRegex(@"^(\+|00)?[0-9]{7,15}$")]
    private static partial Regex PhonePattern();
}
