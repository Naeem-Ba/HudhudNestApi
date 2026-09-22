using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace HudhudNestApi.Domain.Listings.Entities;

/// <summary>
/// Junction table for Property ? Amenity many-to-many relationship.
/// Uses composite PK — no surrogate Id column.
/// </summary>
public class PropertyAmenity
{
    public Guid PropertyId { get; set; }
    public Guid AmenityId { get; set; }

    public Property Property { get; set; } = null!;
    public Amenity Amenity { get; set; } = null!;
}
