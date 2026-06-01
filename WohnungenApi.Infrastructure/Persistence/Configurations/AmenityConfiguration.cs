using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WohnungenApi.Domain.Listings.Entities;

namespace WohnungenApi.Infrastructure.Persistence.Configurations;

public sealed class AmenityConfiguration : IEntityTypeConfiguration<Amenity>
{
    public void Configure(EntityTypeBuilder<Amenity> builder)
    {
        builder.ToTable("Amenities");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.Category)
            .HasMaxLength(100);

        builder.Property(a => a.IconName)
            .HasMaxLength(100);

        // Prevent duplicate amenity names
        builder.HasIndex(a => a.Name).IsUnique();
        builder.HasIndex(a => a.Category);
    }
}
