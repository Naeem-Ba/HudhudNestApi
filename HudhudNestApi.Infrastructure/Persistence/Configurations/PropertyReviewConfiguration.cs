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

        builder.HasIndex(r => r.PropertyId);

        // Soft-delete filter
        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}