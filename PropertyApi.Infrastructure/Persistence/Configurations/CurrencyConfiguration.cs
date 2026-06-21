using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Lookups.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("Currencies");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Code)
            .IsRequired()
            .HasMaxLength(5)
            .IsFixedLength();

        builder.Property(c => c.NameEn).IsRequired().HasMaxLength(100);
        builder.Property(c => c.NameAr).IsRequired().HasMaxLength(100);
        builder.Property(c => c.Symbol).IsRequired().HasMaxLength(10);

        builder.Property(c => c.ExchangeRateToUSD)
            .HasColumnType("decimal(18,6)");

        builder.HasIndex(c => c.Code).IsUnique();
        builder.HasIndex(c => c.IsActive);

        // العلاقة مع Properties (Currency ← Properties)
        builder.HasMany(c => c.Properties)
            .WithOne(p => p.PriceCurrency!)
            .HasForeignKey(p => p.PriceCurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}