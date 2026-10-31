using FluentValidation;
using MediatR;

namespace HudhudNestApi.Application.Contact.Commands.SubmitContact;

public sealed record SubmitContactCommand(
    string Name,
    string Email,
    string? Subject,
    string Message,
    string? IpAddress) : IRequest<Guid>;

public sealed class SubmitContactCommandValidator : AbstractValidator<SubmitContactCommand>
{
    public SubmitContactCommandValidator()
    {
        // The subject is a single line. A line break in it would become a header injection the day
        // the message is forwarded by e-mail (QA Phase 12, P12-D10).
        RuleFor(x => x.Subject)
            .Must(s => s is null || s.IndexOfAny(['\r', '\n']) < 0)
            .WithMessage("الموضوع يجب أن يكون سطرًا واحدًا.")
            .When(x => x.Subject is not null);
    }
}
