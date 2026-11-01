using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Domain.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class AreaPriceStatConfiguration : IEntityTypeConfiguration<AreaPriceStat>
{
    public void Configure(EntityTypeBuilder<AreaPriceStat> builder)
    {
        builder.ToTable("AreaPriceStats", t =>
        {
            t.HasCheckConstraint("CK_AreaPriceStats_Kind", "\"Kind\" IN (1, 2)");
            t.HasCheckConstraint("CK_AreaPriceStats_Quartiles", "\"P25PerSqmUsd\" <= \"MedianPerSqmUsd\" AND \"MedianPerSqmUsd\" <= \"P75PerSqmUsd\"");
            t.HasCheckConstraint("CK_AreaPriceStats_Sample", "\"SampleCount\" > 0");
        });
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).UseIdentityAlwaysColumn();
        builder.Property(s => s.P25PerSqmUsd).HasColumnType("decimal(18,2)");
        builder.Property(s => s.MedianPerSqmUsd).HasColumnType("decimal(18,2)");
        builder.Property(s => s.P75PerSqmUsd).HasColumnType("decimal(18,2)");

        builder.HasOne<Governorate>().WithMany().HasForeignKey(s => s.GovernorateId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<District>().WithMany().HasForeignKey(s => s.DistrictId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<PropertyType>().WithMany().HasForeignKey(s => s.PropertyTypeId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.GovernorateId, s.Kind });
    }
}
