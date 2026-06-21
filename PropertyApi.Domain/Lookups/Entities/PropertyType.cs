using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Domain.Lookups.Entities;

/// <summary>
/// أنواع العقارات بالعربية والإنجليزية.
/// لماذا جدول بدل Enum؟ لأن المستخدم يرى الأنواع في واجهة البحث،
/// ويجب أن تكون قابلة للتعديل من لوحة Admin دون إعادة نشر.
/// </summary>
public class PropertyType
{
    public int Id { get; private set; }
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;

    /// <summary>Residential / Commercial / Land</summary>
    public string Category { get; private set; } = string.Empty;

    /// <summary>اسم الأيقونة (للـ frontend)</summary>
    public string? Icon { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }

    // Navigation
    public ICollection<Property> Properties { get; private set; } = new List<Property>();

    private PropertyType() { }

    public static PropertyType Create(string nameAr, string nameEn,
        string category, string? icon = null, int sortOrder = 0)
    {
        return new PropertyType
        {
            NameAr = nameAr,
            NameEn = nameEn,
            Category = category,
            Icon = icon,
            SortOrder = sortOrder
        };
    }
}