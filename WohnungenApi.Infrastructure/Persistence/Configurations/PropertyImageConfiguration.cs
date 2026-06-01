using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WohnungenApi.Domain.Listings.Entities;

namespace WohnungenApi.Infrastructure.Persistence.Configurations;

public sealed class PropertyImageConfiguration : IEntityTypeConfiguration<PropertyImage>
{
    public void Configure(EntityTypeBuilder<PropertyImage> builder)
    {
        builder.ToTable("PropertyImages");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Url)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(i => i.PublicId)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(i => i.AltText)
            .HasMaxLength(300);

        // Only one main image per property
        builder.HasIndex(i => new { i.PropertyId, i.IsMain })
            .HasFilter("\"IsMain\" = true")   // PostgreSQL syntax
            .IsUnique()
            .HasDatabaseName("IX_PropertyImages_OneMainPerProperty");

        builder.HasIndex(i => i.PropertyId);
    }
}

