using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.SocialDistribution.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations.SocialDistribution;

public sealed class DistributionRunConfiguration : IEntityTypeConfiguration<DistributionRun>
{
    public void Configure(EntityTypeBuilder<DistributionRun> builder)
    {
        builder.ToTable("DistributionRuns");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TriggerType).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.ResultReason).HasMaxLength(200);

        // Restrict: a run is a historical audit record and must survive the property it targeted
        // being later soft-deleted (same rationale as SocialPublication → Property).
        builder.HasOne<Property>().WithMany().HasForeignKey(r => r.PropertyId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.PropertyId);
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.TriggerType);
        builder.HasIndex(r => r.CreatedAt);
    }
}
