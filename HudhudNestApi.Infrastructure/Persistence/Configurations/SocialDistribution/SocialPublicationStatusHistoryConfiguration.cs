using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.SocialDistribution.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations.SocialDistribution;

public sealed class SocialPublicationStatusHistoryConfiguration : IEntityTypeConfiguration<SocialPublicationStatusHistory>
{
    public void Configure(EntityTypeBuilder<SocialPublicationStatusHistory> builder)
    {
        builder.ToTable("SocialPublicationStatusHistories");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.ToStatus).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.Note).HasMaxLength(500);

        // Restrict — the audit trail must survive the publication row conceptually forever;
        // there is no delete path for SocialPublication at all today (soft-delete only, via
        // BaseEntity.IsDeleted), so Restrict here is a safety net, not an expected path.
        builder.HasOne<SocialPublication>().WithMany().HasForeignKey(h => h.SocialPublicationId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(h => h.SocialPublicationId);
        builder.HasIndex(h => h.CreatedAt);
    }
}
