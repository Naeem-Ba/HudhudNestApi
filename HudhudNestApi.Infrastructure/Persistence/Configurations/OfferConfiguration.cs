using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Marketing.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class OfferConfiguration : IEntityTypeConfiguration<Offer>
{
    public void Configure(EntityTypeBuilder<Offer> builder)
    {
        builder.ToTable("Offers");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Name).IsRequired().HasMaxLength(150);
        builder.Property(o => o.Description).HasMaxLength(1000);

        builder.Property(o => o.DiscountType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(o => o.DiscountValue).HasColumnType("numeric(10,2)");

        builder.Property(o => o.TargetPlanTier).HasMaxLength(30);

        builder.Property(o => o.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(o => o.Terms).HasMaxLength(2000);

        // Belt-and-suspenders alongside the atomic conditional UPDATE in
        // TryReserveRedemptionAsync: even a direct/manual write can never leave the row
        // over-redeemed or negative.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Offers_RedeemedCount_NonNegative",
            "\"RedeemedCount\" >= 0"));
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Offers_RedeemedCount_WithinMax",
            "\"MaxRedemptions\" IS NULL OR \"RedeemedCount\" <= \"MaxRedemptions\""));

        builder.HasIndex(o => o.Status);
        builder.HasIndex(o => new { o.Status, o.StartsAtUtc, o.EndsAtUtc });
    }
}
