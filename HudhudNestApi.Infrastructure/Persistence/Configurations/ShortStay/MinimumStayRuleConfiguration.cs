using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.ShortStay.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations.ShortStay;

public sealed class MinimumStayRuleConfiguration : IEntityTypeConfiguration<MinimumStayRule>
{
    public void Configure(EntityTypeBuilder<MinimumStayRule> builder)
    {
        builder.ToTable("ShortStayMinimumStayRules");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.RoomTypeId);
    }
}
