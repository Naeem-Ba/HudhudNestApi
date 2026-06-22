using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Listings.Enums;

namespace PropertyApi.Domain.Listings.Entities;

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
    // -- حقول جديدة ------------------------------------------


    /// <summary>
    /// رابط النسخة المصغرة (thumbnail) من Cloudinary.
    /// يُستخدم في قوائم العقارات لتحميل أسرع.
    /// Cloudinary يُنشئه تلقائياً بـ transformation URL.
    /// </summary>
    public string? ThumbnailUrl { get; set; }

    /// <summary>وصف الصورة (لـ accessibility والـ SEO)</summary>
    public string? Caption { get; set; }

    /// <summary>نوع الصورة — يساعد المستخدم في التصفية</summary>
    public PropertyImageType ImageType { get; set; } = PropertyImageType.General;


    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;



// FK
public Guid PropertyId { get; set; }

    public Property Property { get; set; } = null!;
}
