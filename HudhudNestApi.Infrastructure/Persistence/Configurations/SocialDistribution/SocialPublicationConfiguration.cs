using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.SocialDistribution.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations.SocialDistribution;

public sealed class SocialPublicationConfiguration : IEntityTypeConfiguration<SocialPublication>
{
    public void Configure(EntityTypeBuilder<SocialPublication> builder)
    {
        builder.ToTable("SocialPublications");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.ExternalPostId).HasMaxLength(200);
        builder.Property(p => p.ExternalPostUrl).HasMaxLength(2000);
        builder.Property(p => p.ErrorCode).HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.ErrorMessage).HasMaxLength(500);

        builder.Property(p => p.UtmSource).IsRequired().HasMaxLength(60);
        builder.Property(p => p.UtmMedium).IsRequired().HasMaxLength(60);
        builder.Property(p => p.UtmCampaign).IsRequired().HasMaxLength(60);
        builder.Property(p => p.UtmContent).IsRequired().HasMaxLength(80);

        // Restrict on both — a publication is a historical record and must survive its property
        // or account being (soft-)deleted/deprecated later (same rationale as PropertyShareEvent).
        builder.HasOne<Property>().WithMany().HasForeignKey(p => p.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SocialAccount>().WithMany().HasForeignKey(p => p.SocialAccountId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Content)
            .WithOne()
            .HasForeignKey<SocialPostContent>(c => c.PublicationId)
            .OnDelete(DeleteBehavior.Cascade); // content has no meaning without its publication

        // Phase 4: FK-only (no navigation property) to DistributionRule/DistributionRun — a rule
        // is never hard-deleted (only archived), so this FK never dangles; Restrict rather than
        // Cascade for the same "historical record must survive" reason as Property/SocialAccount
        // above.
        builder.HasOne<DistributionRule>().WithMany().HasForeignKey(p => p.DistributionRuleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DistributionRun>().WithMany().HasForeignKey(p => p.DistributionRunId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.PropertyId);
        builder.HasIndex(p => p.SocialAccountId);
        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.ScheduledAt);
        builder.HasIndex(p => p.CreatedAt);
        builder.HasIndex(p => p.NextRetryAt);
        builder.HasIndex(p => p.UtmSource);
        builder.HasIndex(p => p.UtmCampaign);
        builder.HasIndex(p => p.DistributionRuleId);
        builder.HasIndex(p => p.DistributionRunId);

        // Phase 4 idempotency (spec §12): at most one non-Cancelled, rule-engine-created
        // publication per (property, account) pair at a time. A Cancelled row frees the slot for
        // re-distribution; a manually-created publication (DistributionRuleId null) is untouched
        // by this constraint entirely. Mirrors ISocialPublicationRepository.
        // ExistsActiveForPropertyAndAccountAsync exactly — that check is the primary guard, this
        // partial unique index is defense-in-depth against a genuine race between two concurrent
        // evaluations of the same property.
        // NOTE: Status is persisted as its string name (HasConversion<string>() above), so the
        // filter compares against the enum's name, not its numeric value.
        builder.HasIndex(p => new { p.PropertyId, p.SocialAccountId })
            .HasFilter("\"DistributionRuleId\" IS NOT NULL AND \"Status\" <> 'Cancelled'")
            .IsUnique();

        // Optimistic concurrency (mirrors PropertyConfiguration/TransactionConfiguration): maps
        // the Postgres system column `xmin`. Closes the race between the dispatch worker sweeping
        // this row and an admin cancelling/retrying it at the same moment — one of the two now
        // gets DbUpdateConcurrencyException instead of silently clobbering the other's write.
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();
    }
}
