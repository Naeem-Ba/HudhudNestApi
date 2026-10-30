using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.ShortStay.Entities;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations.ShortStay;

public sealed class ShortStayReviewConfiguration : IEntityTypeConfiguration<ShortStayReview>
{
    public void Configure(EntityTypeBuilder<ShortStayReview> builder)
    {
        builder.ToTable("ShortStayReviews");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Comment).HasMaxLength(2000);

        // One review per booking — enforced at the DB level too, not just in the handler.
        builder.HasIndex(r => r.BookingId).IsUnique();
        builder.HasIndex(r => r.ShortStayListingId);

        // Booking, reviewer and listing had no foreign key (DB audit 2026-10-29). ShortStayListingId
        // and ReviewerId repeat what the booking already says on purpose (listing pages read reviews
        // without walking Booking -> Unit -> RoomType -> Listing); the keys at least stop them
        // pointing at rows that do not exist.
        builder.HasOne<Booking>()
            .WithMany()
            .HasForeignKey(r => r.BookingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(r => r.ReviewerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ShortStayListing>()
            .WithMany()
            .HasForeignKey(r => r.ShortStayListingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t => t.HasCheckConstraint("CK_ShortStayReviews_Rating", "\"Rating\" BETWEEN 1 AND 5"));
    }
}
