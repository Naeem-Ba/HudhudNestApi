namespace HudhudNestApi.Application.ShortStay.Interfaces;

/// <summary>
/// Validates a governorate → district → neighborhood selection against the structured-location
/// catalog and derives the display city from it, so <c>ShortStayListing.City</c> is one of the
/// catalog's own names instead of whatever free text a client typed (guests search by it).
/// </summary>
public interface IShortStayLocationResolver
{
    /// <summary>
    /// Throws <see cref="Common.Exceptions.ValidationException"/> when an id does not exist, is
    /// inactive, or does not belong to its parent. Returns the Arabic governorate name as the city
    /// when a governorate is chosen; otherwise returns <paramref name="fallbackCity"/> trimmed
    /// (null when blank).
    /// </summary>
    Task<string?> ResolveCityAsync(
        int? governorateId, int? districtId, int? neighborhoodId, string? fallbackCity, CancellationToken ct);
}
