namespace HudhudNestApi.Domain.Listings.Enums;

/// <summary>
/// Which listing aggregate a view belongs to. Sale/rent listings (<c>Property</c>) and short-stay
/// listings (<c>ShortStayListing</c>) are separate aggregates with separate ids, so a view is always
/// identified by the pair (kind, id), never by the id alone.
/// </summary>
public enum ListingKind
{
    Property = 1,
    ShortStay = 2
}
