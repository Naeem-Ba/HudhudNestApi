using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.ShortStay.Entities;

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
    }
}
