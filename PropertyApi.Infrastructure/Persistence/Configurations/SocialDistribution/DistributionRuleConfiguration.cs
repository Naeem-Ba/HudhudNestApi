using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Lookups.Entities;
using PropertyApi.Domain.SocialDistribution.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations.SocialDistribution;

public sealed class DistributionRuleConfiguration : IEntityTypeConfiguration<DistributionRule>
{
    public void Configure(EntityTypeBuilder<DistributionRule> builder)
    {
        builder.ToTable("DistributionRules");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Description).HasMaxLength(1000);
        builder.Property(r => r.TransactionType).HasConversion<string>().HasMaxLength(20);

        // FK-only, no navigation property — same bounded-context-isolation pattern as
        // SocialAccount.GovernorateId (Restrict: a rule referencing a since-deactivated lookup
        // row simply never matches anything again; it must not vanish or cascade-delete).
        builder.HasOne<Governorate>().WithMany().HasForeignKey(r => r.ProvinceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PropertyType>().WithMany().HasForeignKey(r => r.PropertyTypeId).OnDelete(DeleteBehavior.Restrict);

        // Restrict: a rule survives its target account being disconnected/suspended — it simply
        // stops producing eligible publications (DistributionEngine skips it) until the account
        // is reconnected, rather than the rule row disappearing.
        builder.HasOne<SocialAccount>().WithMany().HasForeignKey(r => r.SocialAccountId).OnDelete(DeleteBehavior.Restrict);

        // Spec §22/§23: every dimension DistributionRuleRepository.GetActiveCandidatesAsync
        // filters on, indexed for that DB-level pre-filter to stay index-backed as the rule table
        // grows.
        builder.HasIndex(r => r.ProvinceId);
        builder.HasIndex(r => r.PropertyTypeId);
        builder.HasIndex(r => r.TransactionType);
        builder.HasIndex(r => r.SocialAccountId);
        builder.HasIndex(r => r.IsActive);
        builder.HasIndex(r => r.Priority);
        builder.HasIndex(r => new { r.IsActive, r.IsArchived });
    }
}
