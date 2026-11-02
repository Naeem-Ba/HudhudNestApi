using HudhudNestApi.Domain.Demand.Entities;

namespace HudhudNestApi.Application.Demand.DTOs;

/// <summary>The requester's own view of a request.</summary>
public sealed record SearchRequestDto(
    Guid Id,
    string ListingType,
    int GovernorateId,
    int? DistrictId,
    int? NeighborhoodId,
    int? PropertyTypeId,
    decimal? MinBudget,
    decimal MaxBudget,
    string CurrencyCode,
    int? MinRooms,
    decimal? MinArea,
    decimal? MaxArea,
    bool NeedsBalcony,
    bool NeedsElevator,
    bool NeedsParking,
    bool NeedsFurnished,
    string? Details,
    string Status,
    DateTime ExpiresAt,
    DateTime CreatedAt,
    int MatchCount)
{
    public static SearchRequestDto From(PropertySearchRequest r, int matchCount) => new(
        r.Id, r.ListingType.ToString(), r.GovernorateId, r.DistrictId, r.NeighborhoodId, r.PropertyTypeId,
        r.MinBudget, r.MaxBudget, r.CurrencyCode, r.MinRooms, r.MinArea, r.MaxArea,
        r.NeedsBalcony, r.NeedsElevator, r.NeedsParking, r.NeedsFurnished, r.Details,
        r.Status.ToString(), r.ExpiresAt, r.CreatedAt, matchCount);
}

/// <summary>
/// What an advertiser may see of a request: the criteria only. No user id, name, phone or email
/// (DECISIONS.md D-04). <see cref="Id"/> is needed to propose a listing.
/// </summary>
public sealed record AnonymousSearchRequestDto(
    Guid Id,
    string ListingType,
    int GovernorateId,
    int? DistrictId,
    int? NeighborhoodId,
    int? PropertyTypeId,
    decimal? MinBudget,
    decimal MaxBudget,
    string CurrencyCode,
    int? MinRooms,
    decimal? MinArea,
    decimal? MaxArea,
    bool NeedsBalcony,
    bool NeedsElevator,
    bool NeedsParking,
    bool NeedsFurnished,
    string? Details,
    DateTime CreatedAt,
    DateTime ExpiresAt)
{
    public static AnonymousSearchRequestDto From(PropertySearchRequest r) => new(
        r.Id, r.ListingType.ToString(), r.GovernorateId, r.DistrictId, r.NeighborhoodId, r.PropertyTypeId,
        r.MinBudget, r.MaxBudget, r.CurrencyCode, r.MinRooms, r.MinArea, r.MaxArea,
        r.NeedsBalcony, r.NeedsElevator, r.NeedsParking, r.NeedsFurnished, r.Details, r.CreatedAt, r.ExpiresAt);
}

/// <summary>A request that fits one of the advertiser's listings, with the matcher's verdict.</summary>
public sealed record ListingDemandDto(
    AnonymousSearchRequestDto Request,
    int Score,
    IReadOnlyList<string> Reasons,
    bool AlreadyProposed);

/// <summary>A listing suggested for the requester's request (only listings still on the market).</summary>
public sealed record SearchRequestMatchDto(
    Guid PropertyId,
    string Title,
    string ListingType,
    decimal? PurchasePrice,
    decimal? ColdRent,
    decimal? WarmRent,
    string CurrencyCode,
    int? Rooms,
    decimal? Area,
    string? MainImageUrl,
    int Score,
    IReadOnlyList<string> Reasons,
    string Source,
    DateTime MatchedAt);

public sealed record PagedSearchRequests(IReadOnlyList<AnonymousSearchRequestDto> Items, int TotalCount, int Page, int PageSize);
