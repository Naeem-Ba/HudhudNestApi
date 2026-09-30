using FluentValidation;
using HudhudNestApi.Domain.ShortStay.Enums;

namespace HudhudNestApi.Application.ShortStay.Commands.SetPricingRules;

/// <summary>
/// Security audit finding (2026-09-18): this command had no validator at all, so
/// <c>Enum.Parse&lt;PricingRuleType&gt;</c> in the handler threw an unhandled
/// <see cref="ArgumentException"/> on any unrecognized <c>RuleType</c> string, surfacing as an
/// opaque 500 instead of a clean 400 -- same class of bug as
/// UpdateShortStayListingCommandValidator, fixed the same way.
/// </summary>
public sealed class SetPricingRulesCommandValidator : AbstractValidator<SetPricingRulesCommand>
{
    public SetPricingRulesCommandValidator()
    {
        RuleFor(x => x.RoomTypeId).NotEmpty();
        RuleFor(x => x.OwnerId).NotEmpty();

        RuleForEach(x => x.Rules).ChildRules(rule =>
        {
            rule.RuleFor(r => r.RuleType)
                .Must(v => Enum.TryParse<PricingRuleType>(v, ignoreCase: true, out _))
                .WithMessage("قيمة RuleType غير معروفة.");

            rule.RuleFor(r => r.PricePerNight).GreaterThan(0);

            rule.RuleFor(r => r.DateRangeEnd)
                .GreaterThanOrEqualTo(r => r.DateRangeStart)
                .When(r => r.DateRangeStart.HasValue && r.DateRangeEnd.HasValue)
                .WithMessage("تاريخ النهاية يجب أن يكون بعد أو يساوي تاريخ البداية.");
        });
    }
}
