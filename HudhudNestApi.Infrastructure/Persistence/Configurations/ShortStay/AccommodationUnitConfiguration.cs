using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.ShortStay.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations.ShortStay;

public sealed class AccommodationUnitConfiguration : IEntityTypeConfiguration<AccommodationUnit>
{
    public void Configure(EntityTypeBuilder<AccommodationUnit> builder)
    {
        builder.ToTable("AccommodationUnits");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Label).IsRequired().HasMaxLength(100);

        builder.HasMany(u => u.BookingRanges)
            .WithOne(r => r.Unit)
            .HasForeignKey(r => r.UnitId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(u => u.RoomTypeId);
    }
}
