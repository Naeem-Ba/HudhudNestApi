using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class ListingViewCounterConfiguration : IEntityTypeConfiguration<ListingViewCounter>
{
    public void Configure(EntityTypeBuilder<ListingViewCounter> builder)
    {
        builder.ToTable("ListingViewCounters", t =>
            t.HasCheckConstraint("CK_ListingViewCounters_ViewCount_NonNegative", "\"ViewCount\" >= 0"));

        builder.HasKey(c => new { c.Kind, c.ListingId });
        builder.Property(c => c.Kind).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.ViewCount).IsRequired();

        // No FK: one table serves two listing aggregates, and a counter must not block listing deletes.
    }
}
