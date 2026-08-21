using PropertyApi.Domain.Enums;

namespace PropertyApi.Application.Search.DTOs;

public sealed record SavedSearchDto(
    Guid Id,
    string? Name,
    string? CountryCode,
    string? City,
    string? Region,
    int? GovernorateId,
    int? DistrictId,
    int? NeighborhoodId,
    int? PropertyTypeId,
    ListingType? ListingType,
    decimal? MinPrice,
    decimal? MaxPrice,
    string? CurrencyCode,
    int? MinRooms,
    int? MaxRooms,
    decimal? MinArea,
    decimal? MaxArea,
    DateTime CreatedAt,
    DateTime? LastMatchedAt);
