using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WohnungenApi.Domain.Enums;
using WohnungenApi.Domain.Listings.Entities;

namespace WohnungenApi.Infrastructure.Persistence.Configurations;

public sealed class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> builder)
    {
        builder.ToTable("Properties");

        builder.HasKey(p => p.Id);

        // ── Core ─────────────────────────────────────────────
        builder.Property(p => p.Title)
            .IsRequired()
            .HasMaxLength(200);   // Consistent with domain validation

        builder.Property(p => p.Description)
            .IsRequired()
            .HasMaxLength(5000);

        // ── Address ──────────────────────────────────────────
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

        // ── Geo: decimal(9,6) gives precision to ~0.11m ──────
        builder.Property(p => p.Latitude)
            .HasColumnType("decimal(9,6)");

        builder.Property(p => p.Longitude)
            .HasColumnType("decimal(9,6)");

        // ── Pricing ──────────────────────────────────────────
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

        // ── Physical ─────────────────────────────────────────
        builder.Property(p => p.Area)
            .HasColumnType("decimal(10,2)");

        // ── Enums: store as string for readability in DB ──────
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

        // ── Relationships ─────────────────────────────────────
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

        // ── Indexes ──────────────────────────────────────────
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
    }
}