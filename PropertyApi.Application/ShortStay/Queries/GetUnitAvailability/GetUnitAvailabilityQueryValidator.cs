using FluentValidation;

namespace PropertyApi.Application.ShortStay.Queries.GetUnitAvailability;

/// <summary>
/// Security audit finding (2026-09-18): this query had no validator, so an [AllowAnonymous]
/// caller could request an unbounded From/To span (e.g. "0001-01-01" to "9999-12-31") with no
/// rejection -- BookingRepository.GetRangesForUnitAsync does a plain overlap filter with no
/// row/date-span limit of its own. Capping the span here is the same defense-in-depth pattern
/// used for PageSize elsewhere (validator + a repository-side clamp would be redundant here
/// since the repository has no pagination to clamp against -- the span IS the bound).
/// </summary>
public sealed class GetUnitAvailabilityQueryValidator : AbstractValidator<GetUnitAvailabilityQuery>
{
    private const int MaxSpanDays = 366;

    public GetUnitAvailabilityQueryValidator()
    {
        RuleFor(x => x.UnitId).NotEmpty();

        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From)
            .WithMessage("To must be on or after From.");

        RuleFor(x => x)
            .Must(x => x.To.DayNumber - x.From.DayNumber <= MaxSpanDays)
            .WithMessage($"Availability range cannot exceed {MaxSpanDays} days.");
    }
}
