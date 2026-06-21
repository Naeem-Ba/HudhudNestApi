using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Domain.Listings.Enums;


namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> builder)
    {
        builder.ToTable("Properties");

        builder.HasKey(p => p.Id);

        // -- Core ---------------------------------------------
        builder.Property(p => p.Title)
            .IsRequired()
            .HasMaxLength(200);   // Consistent with domain validation

        builder.Property(p => p.Description)
            .IsRequired()
            .HasMaxLength(5000);

        // -- Address ------------------------------------------
        builder.Property(p => p.Street)
            .HasMaxLength(300);

        builder.Property(p => p.City)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(p => p.Region)
            .HasMaxLength(150);

        // ISO 3166-1 alpha-2: fixed 2-char code
        builder.Property(p => p.CountryCode)
            .IsRequired()
            .HasMaxLength(2)
            .IsFixedLength()
            .HasDefaultValue("DE");

        builder.Property(p => p.PostalCode)
            .HasMaxLength(20);

        // -- Geo: decimal(9,6) gives precision to ~0.11m ------
        builder.Property(p => p.Latitude)
            .HasColumnType("decimal(9,6)");

        builder.Property(p => p.Longitude)
            .HasColumnType("decimal(9,6)");

        // -- Pricing ------------------------------------------
        // decimal(18,4) supports global currencies (JPY, SYP have no cents)
        builder.Property(p => p.ColdRent)
            .HasColumnType("decimal(18,4)");

        builder.Property(p => p.WarmRent)
            .HasColumnType("decimal(18,4)");

        builder.Property(p => p.PurchasePrice)
            .HasColumnType("decimal(18,4)");

        builder.Property(p => p.AdditionalCosts)
            .HasColumnType("decimal(18,4)");

        builder.Property(p => p.Deposit)
            .HasColumnType("decimal(18,4)");

        // ISO 4217: fixed 3-char code
        builder.Property(p => p.CurrencyCode)
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength()
            .HasDefaultValue("EUR");

        // -- Physical -----------------------------------------
        builder.Property(p => p.Area)
            .HasColumnType("decimal(10,2)");

        // -- Enums: store as string for readability in DB ------
        // Changing enum order never corrupts data
        builder.Property(p => p.ListingType)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(PropertyStatus.Available);

        builder.Property(p => p.Condition)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.EnergyEfficiency)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.HeatingType)
            .HasConversion<string>()
            .HasMaxLength(20);

        // -- Relationships -------------------------------------
        builder.HasOne(p => p.Owner)
            .WithMany(u => u.Properties)
            .HasForeignKey(p => p.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);  // Don't cascade-delete listings on user delete

        builder.HasMany(p => p.Images)
            .WithOne(i => i.Property)
            .HasForeignKey(i => i.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Messages)
            .WithOne(m => m.Property)
            .HasForeignKey(m => m.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Favorites)
            .WithOne(f => f.Property)
            .HasForeignKey(f => f.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);

        // -- Indexes ------------------------------------------
        // Most common search patterns:
        builder.HasIndex(p => p.CountryCode);
        builder.HasIndex(p => p.City);
        builder.HasIndex(p => new { p.CountryCode, p.City });
        builder.HasIndex(p => p.OwnerId);
        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.ListingType);
        builder.HasIndex(p => p.IsPublished);
        builder.HasIndex(p => p.IsDeleted);
        builder.HasIndex(p => p.CreatedAt);

        // Composite index for the most common filtered search
        builder.HasIndex(p => new { p.CountryCode, p.City, p.Status, p.IsPublished, p.IsDeleted })
            .HasDatabaseName("IX_Properties_Search");

        // ── الوضع القانوني ─────────────────────────────────────────────
        builder.Property(p => p.LegalStatus)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasDefaultValue(LegalStatusType.Unknown);

        builder.Property(p => p.ZoningStatus)
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(ZoningStatusType.Unknown);

        builder.Property(p => p.LegalStatusNotes).HasMaxLength(1000);
        builder.Property(p => p.ZoningNotes).HasMaxLength(500);
        builder.Property(p => p.LegalDisputeNotes).HasMaxLength(1000);

        // ── التفاصيل الإضافية ──────────────────────────────────────────
        builder.Property(p => p.NearestLandmark).HasMaxLength(300);
        builder.Property(p => p.BuildingNumber).HasMaxLength(50);
        builder.Property(p => p.GoogleMapsUrl).HasMaxLength(1000);
        builder.Property(p => p.WaterSource).HasMaxLength(50);
        builder.Property(p => p.InternetType).HasMaxLength(50);
        builder.Property(p => p.ViewDescription).HasMaxLength(300);

        builder.Property(p => p.FurnishingStatus)
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(FurnishingStatus.Unfurnished);

        // ── العلاقات الجديدة ────────────────────────────────────────────
        builder.HasOne(p => p.Governorate)
            .WithMany(g => g.Properties)
            .HasForeignKey(p => p.GovernorateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.District)
            .WithMany(d => d.Properties)
            .HasForeignKey(p => p.DistrictId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Neighborhood)
            .WithMany(n => n.Properties)
            .HasForeignKey(p => p.NeighborhoodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.PropertyType)
            .WithMany(pt => pt.Properties)
            .HasForeignKey(p => p.PropertyTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Agent)
            .WithMany()
            .HasForeignKey(p => p.AgentId)
            .OnDelete(DeleteBehavior.SetNull);

        // ── الفهارس الجديدة ────────────────────────────────────────────
        builder.HasIndex(p => p.GovernorateId);
        builder.HasIndex(p => p.PropertyTypeId);
        builder.HasIndex(p => p.LegalStatus);
        builder.HasIndex(p => p.FurnishingStatus);
        builder.HasIndex(p => p.IsFeatured);
        builder.HasIndex(p => p.IsVerified);
        builder.HasIndex(p => p.PriceCurrencyId);

        // Composite index للبحث في السوق السوري
        builder.HasIndex(p => new { p.GovernorateId, p.PropertyTypeId, p.Status, p.IsPublished })
            .HasDatabaseName("IX_Properties_Syrian_Search");
    }
}
