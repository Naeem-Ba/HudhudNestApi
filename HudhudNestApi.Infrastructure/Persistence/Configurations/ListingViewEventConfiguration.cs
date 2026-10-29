using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class ListingViewEventConfiguration : IEntityTypeConfiguration<ListingViewEvent>
{
    public void Configure(EntityTypeBuilder<ListingViewEvent> builder)
    {
        builder.ToTable("ListingViewEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Kind).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.ViewerKeyHash).IsRequired().HasMaxLength(ListingViewEvent.ViewerKeyHashLength);

        // No FK: a view can target either of two aggregates (Properties, ShortStayListings), and a
        // ledger row must not block or be cascaded by listing deletes (rows age out by retention).

        // The dedupe key. INSERT ... ON CONFLICT DO NOTHING on this index is what makes the counter
        // exactly-once per (listing, viewer, window) under retries and concurrency.
        builder.HasIndex(e => new { e.Kind, e.ListingId, e.ViewerKeyHash, e.Bucket })
            .IsUnique()
            .HasDatabaseName("UX_ListingViewEvents_Dedupe");

        // Stats: per-listing windowed counts and daily trend.
        builder.HasIndex(e => new { e.Kind, e.ListingId, e.ViewedAt })
            .HasDatabaseName("IX_ListingViewEvents_Listing_ViewedAt");

        // Retention sweep.
        builder.HasIndex(e => e.ViewedAt)
            .HasDatabaseName("IX_ListingViewEvents_ViewedAt");
    }
}
