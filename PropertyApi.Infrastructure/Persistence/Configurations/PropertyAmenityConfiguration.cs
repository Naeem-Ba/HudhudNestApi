using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class PropertyAmenityConfiguration : IEntityTypeConfiguration<PropertyAmenity>
{
    public void Configure(EntityTypeBuilder<PropertyAmenity> builder)
    {
        builder.ToTable("PropertyAmenities");

        // Composite PK — no surrogate Id column (cleaner, correct design)
        builder.HasKey(pa => new { pa.PropertyId, pa.AmenityId });

        builder.HasOne(pa => pa.Property)
            .WithMany(p => p.PropertyAmenities)
            .HasForeignKey(pa => pa.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pa => pa.Amenity)
            .WithMany(a => a.PropertyAmenities)
            .HasForeignKey(pa => pa.AmenityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
