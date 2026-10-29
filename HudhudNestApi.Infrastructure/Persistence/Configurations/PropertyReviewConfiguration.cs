using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Reviews.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class PropertyReviewConfiguration : IEntityTypeConfiguration<PropertyReview>
{
    public void Configure(EntityTypeBuilder<PropertyReview> builder)
    {
        builder.ToTable("PropertyReviews");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Rating).IsRequired();
        builder.Property(r => r.Comment).HasMaxLength(1000);

        // Relations
        builder.HasOne(r => r.Property)
            .WithMany()
            .HasForeignKey(r => r.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Reviewer)
            .WithMany()
            .HasForeignKey(r => r.ReviewerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Enforce: one review per user per property
        builder.HasIndex(r => new { r.PropertyId, r.ReviewerId })
            .IsUnique();

        // The domain already refuses ratings outside 1..5; the database now does too (DB audit 2026-10-29).
        builder.ToTable(t => t.HasCheckConstraint("CK_PropertyReviews_Rating", "\"Rating\" BETWEEN 1 AND 5"));

        // Soft-delete filter
        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}