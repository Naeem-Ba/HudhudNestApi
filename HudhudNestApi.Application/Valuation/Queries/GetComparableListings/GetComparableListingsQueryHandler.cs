using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Valuation.DTOs;
using HudhudNestApi.Domain.Valuation.Enums;

namespace HudhudNestApi.Application.Valuation.Queries.GetComparableListings;

/// <summary>
/// Handles GetComparableListingsQuery — the Valuation Fast Path. Pure Query → Handler →
/// IPropertyRepository → Result: no repository of its own (reuses IPropertyRepository, per
/// this phase's explicit constraint), no HTTP/Controller concerns, and no Stage 4 side
/// effects (no invitations, no office matching) — it only ever tells the caller
/// <see cref="ComparableListingsResult.RequiresOfficeValuation"/> so a later handler can act
/// on that; Stage 4 itself is out of scope here.
///
/// Minimum-3-comparables threshold, area tolerance and eligibility rules are this phase's own
/// fixed business rules (see the module's final report) — not sourced from any existing
/// configuration, since none of them existed anywhere in the codebase before this phase.
/// </summary>
public sealed class GetComparableListingsQueryHandler
    : IRequestHandler<GetComparableListingsQuery, ComparableListingsResult>
{
    /// <summary>Section 9's fixed ±20% area window.</summary>
    private const decimal AreaTolerancePercent = 20m;

    /// <summary>Section 13/14 — at least this many comparables before Stage 4 is skipped.</summary>
    private const int MinimumComparablesForFastPath = 3;

    private const string ComparableListingsSource = "📊 مبني على إعلانات مشابهة";

    private const string AskingPriceDisclaimer =
        "الأسعار المعروضة هنا هي أسعار طلب من إعلانات قائمة، وليست أسعار بيع فعلية.";

    private const string LimitedDataMessage =
        "هذا تقييم مبدئي مبني على عدد محدود من الإعلانات المتوفرة حاليًا. " +
        "سيتم إرسال التقييم النهائي خلال الفترة المحددة.";

    private const string NoComparablesMessage =
        "لا توجد حاليًا إعلانات مشابهة كافية لهذا الموقع/النوع لتقديم تقييم مبدئي.";

    private readonly IPropertyRepository _properties;
    private readonly ILogger<GetComparableListingsQueryHandler> _logger;

    public GetComparableListingsQueryHandler(
        IPropertyRepository properties,
        ILogger<GetComparableListingsQueryHandler> logger)
    {
        _properties = properties;
        _logger = logger;
    }

    public async Task<ComparableListingsResult> Handle(
        GetComparableListingsQuery request,
        CancellationToken cancellationToken)
    {
        // Defensive: GovernorateId is the one field this query treats as mandatory (mirrors
        // ValuationInquiry.GovernorateId's own Domain invariant), but nothing here can
        // guarantee the caller actually got it from a valid inquiry. An invalid id simply
        // cannot match anything meaningful — treat it as "no comparables" rather than
        // throwing, the same "never hard-fail an advisory read" philosophy
        // CheckPotentialDuplicatePropertyQueryHandler already applies.
        if (request.GovernorateId <= 0)
        {
            return BuildResult(request, matchLevel: ValuationMatchLevel.Governorate, prices: []);
        }

        var matchLevel = ResolveMatchLevel(request);

        var prices = await _properties.GetComparableListingPricesAsync(
            propertyTypeId: request.PropertyTypeId,
            listingType: request.ListingType,
            area: request.Area,
            areaTolerancePercent: AreaTolerancePercent,
            governorateId: request.GovernorateId,
            districtId: request.DistrictId,
            neighborhoodId: request.NeighborhoodId,
            ct: cancellationToken);

        var result = BuildResult(request, matchLevel, prices);

        _logger.LogInformation(
            "Valuation Fast Path evaluated. InquiryId={InquiryId}, ComparableCount={ComparableCount}, " +
            "MatchLevel={MatchLevel}, RequiresOfficeValuation={RequiresOfficeValuation}",
            request.InquiryId,
            result.ComparableCount,
            result.MatchLevel,
            result.RequiresOfficeValuation);

        return result;
    }

    /// <summary>
    /// Section 8's strict priority: Neighborhood when the inquiry has one, else District, else
    /// Governorate — decided once here from the inquiry's own data, never re-derived per
    /// listing and never silently widened just because a narrower tier returned few results.
    /// </summary>
    private static ValuationMatchLevel ResolveMatchLevel(GetComparableListingsQuery request)
    {
        if (request.NeighborhoodId.HasValue)
            return ValuationMatchLevel.Neighborhood;

        return request.DistrictId.HasValue
            ? ValuationMatchLevel.District
            : ValuationMatchLevel.Governorate;
    }

    private static ComparableListingsResult BuildResult(
        GetComparableListingsQuery request,
        ValuationMatchLevel matchLevel,
        IReadOnlyList<decimal> prices)
    {
        var count = prices.Count;
        var hasComparables = count > 0;
        var requiresOfficeValuation = count < MinimumComparablesForFastPath;

        var result = new ComparableListingsResult
        {
            InquiryId = request.InquiryId,
            HasComparableListings = hasComparables,
            ComparableCount = count,
            MatchLevel = matchLevel,
            IsPreliminary = hasComparables,
            RequiresOfficeValuation = requiresOfficeValuation,
        };

        if (!hasComparables)
        {
            // Section 15 — no fabricated Average/Median/Range from nothing. Min/Max/Average/
            // Median all stay null; no Source/Disclaimer either, since there is no estimate
            // to attribute or caveat.
            result.Message = NoComparablesMessage;
            return result;
        }

        var sorted = prices.OrderBy(p => p).ToList();

        result.MinPrice = sorted[0];
        result.MaxPrice = sorted[^1];
        result.AveragePrice = sorted.Average();
        result.MedianPrice = CalculateMedian(sorted);
        result.Source = ComparableListingsSource;
        result.Disclaimer = AskingPriceDisclaimer;

        if (requiresOfficeValuation)
            result.Message = LimitedDataMessage;

        return result;
    }

    /// <summary>Real median — not Average — over an already-ascending list: the middle value
    /// for an odd count, the average of the two middle values for an even count.</summary>
    private static decimal CalculateMedian(IReadOnlyList<decimal> sortedPrices)
    {
        var count = sortedPrices.Count;
        var mid = count / 2;

        return count % 2 == 0
            ? (sortedPrices[mid - 1] + sortedPrices[mid]) / 2m
            : sortedPrices[mid];
    }
}
