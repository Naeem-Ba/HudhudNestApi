using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Domain.SocialDistribution.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations.SocialDistribution;

public sealed class SocialMediaAssetConfiguration : IEntityTypeConfiguration<SocialMediaAsset>
{
    public void Configure(EntityTypeBuilder<SocialMediaAsset> builder)
    {
        builder.ToTable("SocialMediaAssets");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Platform).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.AssetType).IsRequired().HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.TemplateId).IsRequired().HasMaxLength(100);
        builder.Property(a => a.FileUrl).IsRequired().HasMaxLength(2000);
        builder.Property(a => a.StorageKey).IsRequired().HasMaxLength(300);
        builder.Property(a => a.MimeType).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Checksum).IsRequired().HasMaxLength(64);
        builder.Property(a => a.ErrorMessage).HasMaxLength(500);

        // Restrict: an asset must survive its property being soft-deleted later (historical
        // record of what was actually published) — FK-only, no navigation property.
        builder.HasOne<Property>().WithMany().HasForeignKey(a => a.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SocialPublication>().WithMany().HasForeignKey(a => a.PublicationId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.PropertyId);
        builder.HasIndex(a => a.PublicationId);
        builder.HasIndex(a => a.Checksum);
        builder.HasIndex(a => a.TemplateId);

        // Phase 7 dedupe index (spec §22/§30): the exact tuple ISocialMediaAssetRepository.
        // FindReusableAsync filters on, indexed together so the lookup stays index-backed as the
        // table grows.
        builder.HasIndex(a => new { a.PropertyId, a.Platform, a.AssetType, a.TemplateId, a.TemplateVersion, a.Checksum });
    }
}
