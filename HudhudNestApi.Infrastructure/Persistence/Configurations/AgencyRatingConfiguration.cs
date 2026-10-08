using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Agencies.Entities;
using HudhudNestApi.Domain.Reviews.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class AgencyRatingConfiguration : IEntityTypeConfiguration<AgencyRating>
{
    public void Configure(EntityTypeBuilder<AgencyRating> builder)
    {
        builder.ToTable("AgencyRatings");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Service).IsRequired();
        builder.Property(r => r.Professionalism).IsRequired();
        builder.Property(r => r.Reliability).IsRequired();
        builder.Property(r => r.OverallExperience).IsRequired();
        builder.Ignore(r => r.OverallScore);
        builder.Property(r => r.Comment).HasMaxLength(1000);

        // Agency has no navigation back to its ratings (same one-way style as Property.AgencyId).
        builder.HasOne<Agency>()
            .WithMany()
            .HasForeignKey(r => r.AgencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Rater)
            .WithMany()
            .HasForeignKey(r => r.RaterId)
            .OnDelete(DeleteBehavior.Restrict);

        // One rating per rater and agency: a repeat call edits it, and the database enforces it
        // even if two requests race.
        builder.HasIndex(r => new { r.AgencyId, r.RaterId })
            .IsUnique();
        builder.HasIndex(r => r.AgencyId);

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}
