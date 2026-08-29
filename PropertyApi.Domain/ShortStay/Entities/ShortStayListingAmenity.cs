using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Domain.ShortStay.Entities;

/// <summary>
/// Junction table for ShortStayListing &lt;-&gt; Amenity — reuses the same shared Amenity
/// lookup table already used by traditional Property listings, no duplicate amenity list.
/// </summary>
public class ShortStayListingAmenity
{
    public Guid ShortStayListingId { get; set; }
    public Guid AmenityId { get; set; }

    public ShortStayListing ShortStayListing { get; set; } = null!;
    public Amenity Amenity { get; set; } = null!;
}
