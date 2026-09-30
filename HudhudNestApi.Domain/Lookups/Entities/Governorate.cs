using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HudhudNestApi.Domain.Listings.Entities;


namespace HudhudNestApi.Domain.Lookups.Entities;

/// <summary>
/// المحافظات السورية.
/// لماذا جدول مسطح بدل self-referencing Locations؟
/// لأن المحافظات السورية 14 فقط — لا نحتاج recursive CTE
/// لكل استعلام بسبب جدول صغير وثابت.
/// </summary>
public class Governorate
{
    public int Id { get; private set; }
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2 (سوريا = SY)</summary>
    public string CountryCode { get; private set; } = "SY";

    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    // Navigation
    public ICollection<District> Districts { get; private set; } = new List<District>();
    public ICollection<Property> Properties { get; private set; } = new List<Property>();

    private Governorate() { }

    public static Governorate Create(string nameAr, string nameEn,
        string countryCode = "SY", int sortOrder = 0)
    {
        return new Governorate
        {
            NameAr = nameAr,
            NameEn = nameEn,
            CountryCode = countryCode.ToUpperInvariant(),
            SortOrder = sortOrder
        };
    }
}