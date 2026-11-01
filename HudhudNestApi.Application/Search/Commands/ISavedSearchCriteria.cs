using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Search.Enums;

namespace HudhudNestApi.Application.Search.Commands;

/// <summary>The criteria shared by creating and editing a saved search, so both use one validator.</summary>
public interface ISavedSearchCriteria
{
    string? Name { get; }
    string? CountryCode { get; }
    string? City { get; }
    string? Region { get; }
    int? GovernorateId { get; }
    int? DistrictId { get; }
    int? NeighborhoodId { get; }
    int? PropertyTypeId { get; }
    ListingType? ListingType { get; }
    decimal? MinPrice { get; }
    decimal? MaxPrice { get; }
    string? CurrencyCode { get; }
    int? MinRooms { get; }
    int? MaxRooms { get; }
    decimal? MinArea { get; }
    decimal? MaxArea { get; }
    bool? HasBalcony { get; }
    bool? HasElevator { get; }
    bool? HasParkingSpace { get; }
    SavedSearchFrequency? Frequency { get; }
}
