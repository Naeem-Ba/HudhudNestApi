using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class PropertyShareEventConfiguration : IEntityTypeConfiguration<PropertyShareEvent>
{
    public void Configure(EntityTypeBuilder<PropertyShareEvent> builder)
    {
        builder.ToTable("PropertyShareEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Platform).IsRequired().HasConversion<string>().HasMaxLength(20);

        // UTM / Attribution (Phase 2) — all optional, all already sanitized/length-capped by
        // PropertyShareEvent.SanitizeUtmField before they ever reach here.
        builder.Property(e => e.UtmSource).HasMaxLength(60);
        builder.Property(e => e.UtmMedium).HasMaxLength(60);
        builder.Property(e => e.UtmCampaign).HasMaxLength(60);
        builder.Property(e => e.UtmContent).HasMaxLength(60);

        // Restrict, not cascade — a share event is a historical record and must survive the
        // property being (soft-)deleted later; it should never silently vanish or orphan-cascade.
        builder.HasOne<Property>().WithMany().HasForeignKey(e => e.PropertyId).OnDelete(DeleteBehavior.Restrict);

        // UserId intentionally has no FK — it is nullable (anonymous visitors) and purely
        // informational, never joined for authorization or PII lookups.
        builder.HasIndex(e => e.PropertyId);
        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => new { e.PropertyId, e.Platform });

        // Reporting: "how many shares came from utm_source=facebook / campaign=property_share"
        // (section 18 of the feature spec) — both queried independently of PropertyId.
        builder.HasIndex(e => e.UtmSource);
        builder.HasIndex(e => e.UtmCampaign);
    }
}
