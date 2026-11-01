using HudhudNestApi.Domain.Growth.Entities;
using HudhudNestApi.Domain.Users.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class GrowthEventConfiguration : IEntityTypeConfiguration<GrowthEvent>
{
    public void Configure(EntityTypeBuilder<GrowthEvent> builder)
    {
        builder.ToTable("GrowthEvents", t =>
            t.HasCheckConstraint("CK_GrowthEvents_Name_NotEmpty", "length(\"Name\") > 0"));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityAlwaysColumn();

        builder.Property(e => e.Name).HasMaxLength(GrowthEvent.NameMaxLength).IsRequired();
        builder.Property(e => e.IdempotencyKey).HasMaxLength(GrowthEvent.KeyMaxLength).IsRequired();
        builder.Property(e => e.CorrelationId).HasMaxLength(GrowthEvent.TagMaxLength);
        builder.Property(e => e.Source).HasMaxLength(GrowthEvent.TagMaxLength);
        builder.Property(e => e.Medium).HasMaxLength(GrowthEvent.TagMaxLength);
        builder.Property(e => e.Campaign).HasMaxLength(GrowthEvent.TagMaxLength);

        // Deleting an account keeps the counts and drops the link to the person.
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.IdempotencyKey).IsUnique();
        // Dashboard reads: counts per name over a date range, and per-user activity for retention.
        builder.HasIndex(e => new { e.Name, e.OccurredAt });
        builder.HasIndex(e => new { e.UserId, e.OccurredAt }).HasFilter("\"UserId\" IS NOT NULL");
    }
}
