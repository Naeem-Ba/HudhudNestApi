using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.ShortStay.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations.ShortStay;

public sealed class MinimumStayRuleConfiguration : IEntityTypeConfiguration<MinimumStayRule>
{
    public void Configure(EntityTypeBuilder<MinimumStayRule> builder)
    {
        builder.ToTable("ShortStayMinimumStayRules");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.RoomTypeId);
    }
}
