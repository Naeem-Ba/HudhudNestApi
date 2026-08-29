using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.ShortStay.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations.ShortStay;

/// <summary>
/// The fluent config only maps columns/indexes/FKs — the actual double-booking guarantee is a
/// raw-SQL EXCLUDE constraint added in the AddShortStayAvailability migration (EF Core's fluent
/// API has no first-class support for PostgreSQL EXCLUDE constraints), so do not assume this
/// class is the whole story for concurrency safety here.
/// </summary>
public sealed class UnitBookingRangeConfiguration : IEntityTypeConfiguration<UnitBookingRange>
{
    public void Configure(EntityTypeBuilder<UnitBookingRange> builder)
    {
        builder.ToTable("UnitBookingRanges");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(r => r.Booking)
            .WithMany()
            .HasForeignKey(r => r.BookingId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(r => r.UnitId);
        builder.HasIndex(r => new { r.UnitId, r.CheckIn, r.CheckOut });
    }
}
