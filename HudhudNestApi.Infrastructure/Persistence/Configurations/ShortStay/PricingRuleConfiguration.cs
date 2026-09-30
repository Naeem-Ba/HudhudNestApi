using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.ShortStay.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations.ShortStay;

public sealed class PricingRuleConfiguration : IEntityTypeConfiguration<PricingRule>
{
    public void Configure(EntityTypeBuilder<PricingRule> builder)
    {
        builder.ToTable("ShortStayPricingRules");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.RuleType).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.PricePerNight).HasColumnType("decimal(18,4)");

        builder.HasIndex(r => r.RoomTypeId);
    }
}
