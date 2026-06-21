using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Domain.Lookups.Entities;

public class Neighborhood
{
    public int Id { get; private set; }
    public int DistrictId { get; private set; }
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    // Navigation
    public District? District { get; private set; }
    public ICollection<Property> Properties { get; private set; } = new List<Property>();

    private Neighborhood() { }

    public static Neighborhood Create(int districtId, string nameAr,
        string nameEn = "", int sortOrder = 0)
    {
        return new Neighborhood
        {
            DistrictId = districtId,
            NameAr = nameAr,
            NameEn = nameEn,
            SortOrder = sortOrder
        };
    }
}