using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class PropertyPriceHistoryConfiguration : IEntityTypeConfiguration<PropertyPriceHistory>
{
    public void Configure(EntityTypeBuilder<PropertyPriceHistory> builder)
    {
        builder.ToTable("PropertyPriceHistories");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.PriceField).IsRequired().HasMaxLength(30);
        builder.Property(h => h.OldValue).HasColumnType("decimal(18,4)");
        builder.Property(h => h.NewValue).HasColumnType("decimal(18,4)");
        builder.Property(h => h.CurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength();

        builder.HasOne(h => h.Property)
            .WithMany()
            .HasForeignKey(h => h.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);

        // Matches the same pattern already used by RentalDetailsConfiguration /
        // SaleDetailsConfiguration / Favorite: a price-history row is meaningless
        // without its (non-deleted) property, so it should disappear from queries
        // the moment the property is soft-deleted, exactly like Property itself does.
        // Without this, EF warns that a required dependent (PropertyPriceHistory)
        // has no filter matching its required principal's (Property) filter.
        builder.HasQueryFilter(h => !h.Property!.IsDeleted);

        // Primary access pattern: "show the price timeline for this property, newest first".
        builder.HasIndex(h => new { h.PropertyId, h.ChangedAt });
    }
}
