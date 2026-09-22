using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Domain.Lookups.Entities;

/// <summary>
/// أنواع العقارات بالعربية والإنجليزية.
/// لماذا جدول بدل Enum؟ لأن المستخدم يرى الأنواع في واجهة البحث،
//— ويجب أن تكون قابلة للتعديل من لوحة Admin دون إعادة نشر.
/// </summary>
public class PropertyType
{
    public int Id { get; private set; }

    /// <summary>
    /// Stable machine-readable code for APIs and frontend.
    /// Example: apartment, villa, shop.
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;

    /// <summary>Residential / Commercial / Land</summary>
    public string Category { get; private set; } = string.Empty;

    /// <summary>اسم الأيقونة للـ Frontend</summary>
    public string? Icon { get; private set; }

    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }

    public ICollection<Property> Properties { get; private set; } = new List<Property>();

    private PropertyType() { }

    public static PropertyType Create(
        string code,
        string nameAr,
        string nameEn,
        string category,
        string? icon = null,
        int sortOrder = 0)
    {
        return new PropertyType
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