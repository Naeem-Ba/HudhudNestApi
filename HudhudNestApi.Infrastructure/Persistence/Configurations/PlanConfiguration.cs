using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Plans.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("Plans");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Tier)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(p => p.NameKey)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.TaglineKey)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.PriceKind)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.PriceUsd)
            .HasColumnType("numeric(10,2)");

        // Npgsql maps string[] to a native Postgres text[] column — no extra
        // configuration needed. These are i18n keys, never displayed text, so a
        // relational child table would be pure overhead for data this shape.
        builder.Property(p => p.FeatureKeys)
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(p => p.NoteKey)
            .HasMaxLength(100);

        builder.Property(p => p.CtaKey)
            .HasMaxLength(100)
            .IsRequired();

        // Null means unlimited (see Plan.ListingLimit's doc comment) — the check constraint
        // only needs to rule out zero/negative, not require a value.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Plans_ListingLimit_PositiveOrUnlimited",
            "\"ListingLimit\" IS NULL OR \"ListingLimit\" > 0"));

        builder.HasIndex(p => p.Tier)
            .IsUnique();

        builder.HasIndex(p => p.IsActive);
    }
}
