using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Marketing.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class MarketingEventConfiguration : IEntityTypeConfiguration<MarketingEvent>
{
    public void Configure(EntityTypeBuilder<MarketingEvent> builder)
    {
        builder.ToTable("MarketingEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Source).IsRequired().HasMaxLength(60);
        builder.Property(e => e.Campaign).HasMaxLength(120);
        builder.Property(e => e.SessionId).HasMaxLength(64);
        builder.Property(e => e.Path).HasMaxLength(300);

        // Restrict, not cascade — an event is a historical record and must not disappear
        // (or silently orphan-cascade) if the Lead/Offer it references is ever removed.
        builder.HasOne<Lead>().WithMany().HasForeignKey(e => e.LeadId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Offer>().WithMany().HasForeignKey(e => e.OfferId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => e.EventType);
        builder.HasIndex(e => new { e.EventType, e.CreatedAt });
        builder.HasIndex(e => e.SessionId);
    }
}
