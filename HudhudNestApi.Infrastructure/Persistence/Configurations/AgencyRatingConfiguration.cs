using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Agencies.Entities;

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
        // Computed in C# from the stored criteria, never persisted.
        builder.Ignore(r => r.OverallScore);
        builder.Property(r => r.Comment).HasMaxLength(AgencyRating.CommentMaxLength);

        // Restrict, not Cascade: ratings are history and an office is only ever deactivated, but a hard
        // delete of an office or a rater must be a deliberate, separate cleanup (same stance as UserRatings).
        builder.HasOne(r => r.Agency)
            .WithMany()
            .HasForeignKey(r => r.AgencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Rater)
            .WithMany()
            .HasForeignKey(r => r.RaterId)
            .OnDelete(DeleteBehavior.Restrict);

        // One rating per rater per office; the second submit edits it. The name is mapped to a 409 in
        // UnitOfWork for the race between two first-time submissions.
        builder.HasIndex(r => new { r.AgencyId, r.RaterId })
            .IsUnique()
            .HasDatabaseName("IX_AgencyRatings_AgencyId_RaterId");

        // The office page lists newest first.
        builder.HasIndex(r => new { r.AgencyId, r.CreatedAt })
            .HasDatabaseName("IX_AgencyRatings_AgencyId_CreatedAt");

        builder.HasIndex(r => r.RaterId);

        builder.ToTable(t =>
        {
            foreach (var column in new[] { "Service", "Professionalism", "Reliability", "OverallExperience" })
                t.HasCheckConstraint($"CK_AgencyRatings_{column}", $"\"{column}\" BETWEEN 1 AND 5");
        });

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}
