using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WohnungenApi.Domain.Common.Entities;

namespace WohnungenApi.Domain.Listings.Entities;

/// <summary>
/// Image attached to a property listing.
/// PublicId is the CDN/cloud storage reference (e.g. Cloudinary public_id).
/// </summary>
public class PropertyImage : BaseEntity
{
    /// <summary>Full URL to the image (CDN URL).</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Cloud storage public identifier for deletion/management.</summary>
    public string PublicId { get; set; } = string.Empty;

    /// <summary>Alt text for accessibility and SEO.</summary>
    public string? AltText { get; set; }

    /// <summary>True for the primary thumbnail image displayed in search results.</summary>
    public bool IsMain { get; set; } = false;

    /// <summary>Display order (0 = first).</summary>
    public int SortOrder { get; set; } = 0;

    // FK
    public Guid PropertyId { get; set; }
    public Property Property { get; set; } = null!;
}
