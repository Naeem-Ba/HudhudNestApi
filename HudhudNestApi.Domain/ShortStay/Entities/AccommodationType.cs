namespace HudhudNestApi.Domain.ShortStay.Entities;

/// <summary>
/// أنواع أماكن الإقامة القصيرة (شاليه، فيلا، فندق...).
/// جدول lookup إداري وليس enum ثابت — لنفس سبب PropertyType: قابل للتعديل من لوحة
/// Admin دون إعادة نشر، ويظهر للمستخدم مباشرة في واجهة البحث.
/// Category يجمع الأنواع في مجموعات: PrivateResidence / Tourism / Hospitality / Other.
/// </summary>
public class AccommodationType
{
    public int Id { get; private set; }

    /// <summary>Stable machine-readable code for APIs and frontend. Example: chalet, villa.</summary>
    public string Code { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;

    /// <summary>PrivateResidence / Tourism / Hospitality / Other</summary>
    public string Category { get; private set; } = string.Empty;

    public string? Icon { get; private set; }

    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }

    public ICollection<ShortStayListing> Listings { get; private set; } = new List<ShortStayListing>();

    private AccommodationType() { }

    public static AccommodationType Create(
        string code,
        string nameAr,
        string nameEn,
        string category,
        string? icon = null,
        int sortOrder = 0)
    {
        return new AccommodationType
        {
            Code = code.Trim().ToLowerInvariant(),
            NameAr = nameAr,
            NameEn = nameEn,
            Category = category,
            Icon = icon,
            SortOrder = sortOrder
        };
    }
}
