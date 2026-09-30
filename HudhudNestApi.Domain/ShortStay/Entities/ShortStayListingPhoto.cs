using HudhudNestApi.Domain.Common.Entities;

namespace HudhudNestApi.Domain.ShortStay.Entities;

/// <summary>
/// Photo attached to a short-stay listing. Mirrors PropertyImage's shape (the same
/// Cloudinary upload pipeline is reused) as a parallel table rather than retrofitting
/// PropertyImage into a polymorphic owner — lower risk to the existing Property image
/// pipeline.
/// </summary>
public class ShortStayListingPhoto : BaseEntity
{
    public string Url { get; set; } = string.Empty;
    public string PublicId { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public string? AltText { get; set; }
    public bool IsMain { get; set; }
    public int SortOrder { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public Guid ShortStayListingId { get; set; }
    public ShortStayListing ShortStayListing { get; set; } = null!;
}
