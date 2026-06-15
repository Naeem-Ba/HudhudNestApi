using FluentValidation;

namespace PropertyApi.Application.Bookings.Commands.RequestVisit;

public sealed class RequestVisitCommandValidator : AbstractValidator<RequestVisitCommand>
{
    public RequestVisitCommandValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty();
        RuleFor(x => x.RequesterId).NotEmpty();

        RuleFor(x => x.ProposedAt)
            .GreaterThan(DateTime.UtcNow.AddHours(2))
            .WithMessage("ÙŠØ¬Ø¨ Ø£Ù† ÙŠÙƒÙˆÙ† Ø§Ù„Ø­Ø¬Ø² Ù‚Ø¨Ù„ Ù…ÙˆØ¹Ø¯ Ø§Ù„Ø²ÙŠØ§Ø±Ø© Ø¨Ø³Ø§Ø¹ØªÙŠÙ† Ø¹Ù„Ù‰ Ø§Ù„Ø£Ù‚Ù„.")
            .LessThan(DateTime.UtcNow.AddDays(90))
            .WithMessage("Ù„Ø§ ÙŠÙ…ÙƒÙ† Ø§Ù„Ø­Ø¬Ø² Ù„Ø£ÙƒØ«Ø± Ù…Ù† 90 ÙŠÙˆÙ…Ù‹Ø§ Ù…Ù‚Ø¯Ù…Ù‹Ø§.");

        RuleFor(x => x.VisitorName)
            .NotEmpty().WithMessage("Ø§Ù„Ø§Ø³Ù… Ù…Ø·Ù„ÙˆØ¨.")
            .MaximumLength(150);

        RuleFor(x => x.VisitorPhone)
            .NotEmpty().WithMessage("Ø±Ù‚Ù… Ø§Ù„Ù‡Ø§ØªÙ Ù…Ø·Ù„ÙˆØ¨.")
            .Matches(@"^\\+?[0-9\\s\\-]{7,20}$")
            .WithMessage("ØµÙŠØºØ© Ø±Ù‚Ù… Ø§Ù„Ù‡Ø§ØªÙ ØºÙŠØ± ØµØ­ÙŠØ­Ø©.");

        RuleFor(x => x.VisitorNote)
            .MaximumLength(500)
            .When(x => x.VisitorNote is not null);
    }
}

