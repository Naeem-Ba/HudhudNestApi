using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.ShortStay.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations.ShortStay;

public sealed class ShortStayListingAmenityConfiguration : IEntityTypeConfiguration<ShortStayListingAmenity>
{
    public void Configure(EntityTypeBuilder<ShortStayListingAmenity> builder)
    {
        builder.ToTable("ShortStayListingAmenities");
        builder.HasKey(x => new { x.ShortStayListingId, x.AmenityId });

        builder.HasOne(x => x.ShortStayListing)
            .WithMany(l => l.ListingAmenities)
            .HasForeignKey(x => x.ShortStayListingId)
            .OnDelete(DeleteBehavior.Cascade);

        // Points at the SAME shared Amenity lookup table used by PropertyAmenity — no
        // duplicate amenity master list for short-stay listings.
        builder.HasOne(x => x.Amenity)
            .WithMany()
            .HasForeignKey(x => x.AmenityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
