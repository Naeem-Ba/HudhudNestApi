using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Search.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

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

        builder.HasOne<PropertyApi.Domain.Users.Entities.UserAccount>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Primary access patterns: "my saved searches" and the matcher's periodic full scan.
        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.LastMatchedAt);
    }
}
