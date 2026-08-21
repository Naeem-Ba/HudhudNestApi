using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Reviews.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class UserRatingConfiguration : IEntityTypeConfiguration<UserRating>
{
    public void Configure(EntityTypeBuilder<UserRating> builder)
    {
        builder.ToTable("UserRatings");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Credibility).IsRequired();
        builder.Property(r => r.Safety).IsRequired();
        builder.Property(r => r.ResponseSpeed).IsRequired();
        builder.Property(r => r.Transparency).IsRequired();
        builder.Property(r => r.Comment).HasMaxLength(1000);

        // Relations — both point at UserAccount, so both must be Restrict
        // (Cascade on two paths to the same table is rejected by SQL Server/
        // Postgres alike as a multiple-cascade-path cycle).
        builder.HasOne(r => r.RatedUser)
            .WithMany()
            .HasForeignKey(r => r.RatedUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Rater)
            .WithMany()
            .HasForeignKey(r => r.RaterId)
            .OnDelete(DeleteBehavior.Restrict);

        // Enforce: one rating per rater per rated user
        builder.HasIndex(r => new { r.RatedUserId, r.RaterId })
            .IsUnique();

        builder.HasIndex(r => r.RatedUserId);

        // Soft-delete filter
        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}
