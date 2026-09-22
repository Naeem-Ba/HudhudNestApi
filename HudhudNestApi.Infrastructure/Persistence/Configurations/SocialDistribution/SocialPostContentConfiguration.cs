using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.SocialDistribution.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations.SocialDistribution;

public sealed class SocialPostContentConfiguration : IEntityTypeConfiguration<SocialPostContent>
{
    public void Configure(EntityTypeBuilder<SocialPostContent> builder)
    {
        builder.ToTable("SocialPostContents");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Title).IsRequired().HasMaxLength(300);
        builder.Property(c => c.Body).IsRequired().HasMaxLength(10_000);
        builder.Property(c => c.ImageUrl).IsRequired().HasMaxLength(2000);
        builder.Property(c => c.TargetUrl).IsRequired().HasMaxLength(2000);
        builder.Property(c => c.Hashtags).IsRequired().HasMaxLength(1000);
        builder.Property(c => c.Language).IsRequired().HasMaxLength(5);
        builder.Property(c => c.Platform).IsRequired().HasConversion<string>().HasMaxLength(20);

        // Phase 8: Human Review workflow.
        builder.Property(c => c.ReviewStatus).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.ReviewNote).HasMaxLength(500);

        // The PublicationId FK/unique-index side of the 1:1 relationship is declared in
        // SocialPublicationConfiguration (builder.HasOne(p => p.Content).WithOne()...) — EF Core
        // needs it declared from exactly one side to avoid a duplicate/conflicting mapping.
        builder.HasIndex(c => c.PublicationId).IsUnique();

        // Phase 7: FK-only (no navigation property) to the generated asset currently backing
        // ImageUrl — SetNull rather than Restrict, since an asset expiring/being cleaned up later
        // must not block deleting it just because a past (possibly already-Published) content row
        // still points at it.
        builder.HasOne<SocialMediaAsset>().WithMany().HasForeignKey(c => c.SocialMediaAssetId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(c => c.SocialMediaAssetId);
    }
}
