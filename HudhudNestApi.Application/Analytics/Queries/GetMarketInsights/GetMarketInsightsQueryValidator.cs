using FluentValidation;
using HudhudNestApi.Application.Common.Validation;

namespace HudhudNestApi.Application.Analytics.Queries.GetMarketInsights;

/// <summary>
/// P18-D09: <c>?countryCode=%00</c> on this anonymous endpoint reached PostgreSQL, failed with
/// 22021 (logged as an error for every hostile request) and only then became a 422. The text is
/// now rejected before any database call. A country code is ISO-3166 alpha-2/3, so a short cap is safe.
/// </summary>
public sealed class GetMarketInsightsQueryValidator : AbstractValidator<GetMarketInsightsQuery>
{
    public GetMarketInsightsQueryValidator()
    {
        RuleFor(x => x.CountryCode)
            .MaximumLength(8).WithMessage("countryCode is too long.").WithErrorCode("TOO_LONG")
            .NoControlCharacters();
    }
}
