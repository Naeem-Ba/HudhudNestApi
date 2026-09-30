using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HudhudNestApi.Domain.Listings.Entities;


namespace HudhudNestApi.Domain.Lookups.Entities;

public class District
{
    public int Id { get; private set; }
    public int GovernorateId { get; private set; }
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    // Navigation
    public Governorate? Governorate { get; private set; }
    public ICollection<Neighborhood> Neighborhoods { get; private set; } = new List<Neighborhood>();
    public ICollection<Property> Properties { get; private set; } = new List<Property>();

    private District() { }

    public static District Create(int governorateId, string nameAr,
        string nameEn, int sortOrder = 0)
    {
        return new District
        {
            GovernorateId = governorateId,
            NameAr = nameAr,
            NameEn = nameEn,
            SortOrder = sortOrder
        };
    }
}