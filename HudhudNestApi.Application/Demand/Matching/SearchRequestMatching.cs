using HudhudNestApi.Application.Listings.Currencies;
using HudhudNestApi.Domain.Demand.Entities;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Enums;

namespace HudhudNestApi.Application.Demand.Matching;

/// <summary>What the matcher needs to know about a listing. Null means "the listing does not say".</summary>
public sealed record ListingFacts(
    Guid PropertyId,
    Guid OwnerId,
    ListingType ListingType,
    int? GovernorateId,
    int? DistrictId,
    int? NeighborhoodId,
    int? PropertyTypeId,
    decimal? SalePrice,
    /// <summary>Monthly rent: the warm rent when given, otherwise the cold rent.</summary>
    decimal? RentPrice,
    string CurrencyCode,
    int? Rooms,
    decimal? Area,
    bool HasBalcony,
    bool HasElevator,
    bool HasParkingSpace,
    FurnishingStatus FurnishingStatus);

/// <summary>
/// Weights and thresholds of the matcher. Bound from configuration section "SearchRequests:Matching" so they
/// can be tuned without a code change. The four weights are points out of 100 a listing can earn.
/// </summary>
public sealed class SearchRequestMatchingOptions
{
    public const string SectionName = "SearchRequests:Matching";

    public int LocationWeight { get; set; } = 30;
    public int BudgetWeight { get; set; } = 25;
    public int SizeWeight { get; set; } = 25;
    public int FeatureWeight { get; set; } = 20;

    /// <summary>A listing up to this share above the budget still matches (as "slightly above").</summary>
    public decimal BudgetTolerance { get; set; } = 0.05m;

    /// <summary>Below this score nobody is notified (the match is still not stored).</summary>
    public int MinimumScore { get; set; } = 50;
}

public sealed record MatchResult(int Score, IReadOnlyList<string> Reasons);

/// <summary>
/// Growth engine P2 matcher (DECISIONS.md D-05). Hard rules exclude: listing type, governorate, property type
/// when the request names one, a price that is known and inside the budget (with tolerance), rooms or area
/// that are known and outside the request. Everything else scores. A criterion the listing does not state is
/// "unknown": it earns no points and is listed as a reason, never counted as a match.
/// </summary>
public static class SearchRequestMatcher
{
    public static MatchResult? Evaluate(
        PropertySearchRequest request, ListingFacts listing, CurrencyRates rates, SearchRequestMatchingOptions options)
    {
        var reasons = new List<string>();

        if (!ListingTypeFits(request.ListingType, listing.ListingType))
            return null;
        if (listing.GovernorateId != request.GovernorateId)
            return null;
        if (request.PropertyTypeId.HasValue && listing.PropertyTypeId != request.PropertyTypeId)
            return null;

        // Budget: a listing without a price, or in a currency that cannot be converted, cannot be confirmed
        // to fit, so it is not a match (the requester asked for a budget).
        var listedPrice = request.ListingType == ListingType.ForSale ? listing.SalePrice : listing.RentPrice;
        if (!listedPrice.HasValue || listedPrice <= 0)
            return null;
        var price = rates.Convert(listedPrice.Value, listing.CurrencyCode, request.CurrencyCode);
        if (price is null)
            return null;
        var ceiling = request.MaxBudget * (1 + options.BudgetTolerance);
        if (price > ceiling)
            return null;
        if (request.MinBudget.HasValue && price < request.MinBudget.Value * (1 - options.BudgetTolerance))
            return null;

        if (request.MinRooms.HasValue && listing.Rooms.HasValue && listing.Rooms < request.MinRooms)
            return null;
        if (listing.Area.HasValue &&
            ((request.MinArea.HasValue && listing.Area < request.MinArea) || (request.MaxArea.HasValue && listing.Area > request.MaxArea)))
            return null;

        double score = 0;

        // Location precision.
        if (request.NeighborhoodId.HasValue)
        {
            if (listing.NeighborhoodId == request.NeighborhoodId) { score += options.LocationWeight; reasons.Add("location_neighborhood"); }
            else if (request.DistrictId.HasValue && listing.DistrictId == request.DistrictId) { score += options.LocationWeight * 0.6; reasons.Add("location_district"); }
            else { score += options.LocationWeight * 0.2; reasons.Add("location_governorate"); }
        }
        else if (request.DistrictId.HasValue)
        {
            if (listing.DistrictId == request.DistrictId) { score += options.LocationWeight; reasons.Add("location_district"); }
            else { score += options.LocationWeight * 0.3; reasons.Add("location_governorate"); }
        }
        else
        {
            score += options.LocationWeight;
            reasons.Add("location_governorate");
        }

        // Budget position: full points inside the budget, half when only within the tolerance.
        if (price <= request.MaxBudget) { score += options.BudgetWeight; reasons.Add("budget_within"); }
        else { score += options.BudgetWeight * 0.5; reasons.Add("budget_slightly_above"); }

        // Size: rooms and area share the weight; a criterion the request does not set earns its share.
        var sizeParts = 0.0;
        if (!request.MinRooms.HasValue) sizeParts += 0.5;
        else if (listing.Rooms.HasValue) { sizeParts += 0.5; reasons.Add("rooms_fit"); }
        else reasons.Add("rooms_unknown");

        if (!request.MinArea.HasValue && !request.MaxArea.HasValue) sizeParts += 0.5;
        else if (listing.Area.HasValue) { sizeParts += 0.5; reasons.Add("area_fit"); }
        else reasons.Add("area_unknown");
        score += options.SizeWeight * sizeParts;

        // Features: a ticked feature on the listing is a match; an unticked one is unknown (the form has
        // no "no" answer), so it earns nothing and is reported.
        var wanted = new List<(bool Wanted, bool Has, string Code)>
        {
            (request.NeedsBalcony, listing.HasBalcony, "balcony"),
            (request.NeedsElevator, listing.HasElevator, "elevator"),
            (request.NeedsParking, listing.HasParkingSpace, "parking"),
            (request.NeedsFurnished, listing.FurnishingStatus is FurnishingStatus.Furnished or FurnishingStatus.SemiFurnished, "furnished"),
        }.Where(f => f.Wanted).ToList();

        if (wanted.Count == 0)
        {
            score += options.FeatureWeight;
        }
        else
        {
            var met = 0;
            foreach (var feature in wanted)
            {
                if (feature.Has) { met++; reasons.Add($"{feature.Code}_yes"); }
                else reasons.Add($"{feature.Code}_unknown");
            }
            score += options.FeatureWeight * (double)met / wanted.Count;
        }

        var total = options.LocationWeight + options.BudgetWeight + options.SizeWeight + options.FeatureWeight;
        var normalized = total <= 0 ? 0 : (int)Math.Round(score * 100 / total, MidpointRounding.AwayFromZero);
        return new MatchResult(Math.Clamp(normalized, 0, 100), reasons);
    }

    public static bool ListingTypeFits(ListingType wanted, ListingType offered)
        => offered == wanted || offered == ListingType.ForRentAndSale;
}
