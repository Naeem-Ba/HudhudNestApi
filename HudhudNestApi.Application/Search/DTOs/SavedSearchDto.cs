using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Search.Enums;

namespace HudhudNestApi.Application.Search.DTOs;

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
    DateTime? LastMatchedAt,
    bool? HasBalcony = null,
    bool? HasElevator = null,
    bool? HasParkingSpace = null,
    SavedSearchFrequency Frequency = SavedSearchFrequency.Instant,
    bool IsPaused = false,
    DateTime? LastNotifiedAt = null,
    int NotifiedCount = 0);
