namespace PropertyApi.Application.ShortStay.DTOs;

/// <summary>
/// Search filter for published, bookable listings. When CheckIn/CheckOut are both set, results
/// are additionally restricted to listings that have at least one Unit free for that exact
/// range — filtering happens entirely in the database (see
/// IShortStayListingRepository.SearchAsync), never by loading everything into memory.
/// </summary>
public sealed class ShortStayListingSearchFilter
{
    public string? City { get; init; }
    public int? AccommodationTypeId { get; init; }
    public DateOnly? CheckIn { get; init; }
    public DateOnly? CheckOut { get; init; }
    public int? Guests { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
