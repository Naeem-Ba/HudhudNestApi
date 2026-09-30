using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.ShortStay.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations.ShortStay;

public sealed class RoomTypeConfiguration : IEntityTypeConfiguration<RoomType>
{
    public void Configure(EntityTypeBuilder<RoomType> builder)
    {
        builder.ToTable("ShortStayRoomTypes");
        builder.HasKey(rt => rt.Id);

        builder.Property(rt => rt.Name).IsRequired().HasMaxLength(100);
        builder.Property(rt => rt.BasePricePerNight).HasColumnType("decimal(18,4)");

        builder.HasMany(rt => rt.Units)
            .WithOne(u => u.RoomType)
            .HasForeignKey(u => u.RoomTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(rt => rt.PricingRules)
            .WithOne(r => r.RoomType)
            .HasForeignKey(r => r.RoomTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(rt => rt.MinimumStayRules)
            .WithOne(r => r.RoomType)
            .HasForeignKey(r => r.RoomTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(rt => rt.ShortStayListingId);
    }
}
