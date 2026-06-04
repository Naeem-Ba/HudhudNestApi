using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PropertyApi.Domain.Common.Entities;

namespace PropertyApi.Domain.Listings.Entities;

/// <summary>
/// Lookup/reference table for property amenities (WiFi, Pool, Gym, etc.).
/// Category groups amenities for filtering (e.g. "Security", "Comfort", "Appliances").
/// </summary>
public class Amenity : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }  // e.g. "Security", "Comfort", "Kitchen"
    public string? IconName { get; set; }  // CSS/icon class name for frontend

    // Navigation — only through junction table, not directly
    public ICollection<PropertyAmenity> PropertyAmenities { get; set; } = new List<PropertyAmenity>();
}
