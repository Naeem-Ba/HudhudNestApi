using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class PropertyAttributionEventConfiguration : IEntityTypeConfiguration<PropertyAttributionEvent>
{
    public void Configure(EntityTypeBuilder<PropertyAttributionEvent> builder)
    {
        builder.ToTable("PropertyAttributionEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType).IsRequired().HasConversion<string>().HasMaxLength(24);

        builder.Property(e => e.UtmSource).HasMaxLength(60);
        builder.Property(e => e.UtmMedium).HasMaxLength(60);
        builder.Property(e => e.UtmCampaign).HasMaxLength(60);
        builder.Property(e => e.UtmContent).HasMaxLength(60);

        // Restrict, not cascade — same rationale as PropertyShareEventConfiguration: a historical
        // analytics record must survive the property being (soft-)deleted later.
        builder.HasOne<Property>().WithMany().HasForeignKey(e => e.PropertyId).OnDelete(DeleteBehavior.Restrict);

        // UserId intentionally has no FK — nullable (anonymous visitors), purely informational.
        builder.HasIndex(e => e.PropertyId);
        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => e.EventType);
        builder.HasIndex(e => new { e.PropertyId, e.EventType });

        // Reporting: "views/contacts from utm_source=facebook / campaign=property_share".
        builder.HasIndex(e => e.UtmSource);
        builder.HasIndex(e => e.UtmCampaign);
    }
}
