using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Domain.Search.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class SavedSearchConfiguration : IEntityTypeConfiguration<SavedSearch>
{
    public void Configure(EntityTypeBuilder<SavedSearch> builder)
    {
        builder.ToTable("SavedSearches");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).HasMaxLength(150);
        builder.Property(s => s.CountryCode).HasMaxLength(2).IsFixedLength();
        builder.Property(s => s.City).HasMaxLength(150);
        builder.Property(s => s.Region).HasMaxLength(150);
        builder.Property(s => s.CurrencyCode).HasMaxLength(3).IsFixedLength();

        builder.Property(s => s.MinPrice).HasColumnType("decimal(18,4)");
        builder.Property(s => s.MaxPrice).HasColumnType("decimal(18,4)");
        builder.Property(s => s.MinArea).HasColumnType("decimal(10,2)");
        builder.Property(s => s.MaxArea).HasColumnType("decimal(10,2)");

        builder.Property(s => s.ListingType).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<HudhudNestApi.Domain.Users.Entities.UserAccount>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The location and type filters had no foreign key (DB audit 2026-10-29). Restrict, like
        // Properties: lookups are seed rows and nothing in the code deletes them.
        builder.HasOne<Governorate>().WithMany().HasForeignKey(s => s.GovernorateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<District>().WithMany().HasForeignKey(s => s.DistrictId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Neighborhood>().WithMany().HasForeignKey(s => s.NeighborhoodId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PropertyType>().WithMany().HasForeignKey(s => s.PropertyTypeId).OnDelete(DeleteBehavior.Restrict);

        // Primary access patterns: "my saved searches" and the matcher's periodic full scan.
        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.LastMatchedAt);
    }
}
