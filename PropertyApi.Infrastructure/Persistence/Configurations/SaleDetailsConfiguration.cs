using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class SaleDetailsConfiguration : IEntityTypeConfiguration<SaleDetails>
{
    public void Configure(EntityTypeBuilder<SaleDetails> builder)
    {
        builder.ToTable("SaleDetails");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.TotalPrice).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(s => s.DownPaymentPercentage).HasColumnType("decimal(5,2)");
        builder.Property(s => s.DownPaymentAmount).HasColumnType("decimal(18,2)");
        builder.Property(s => s.MonthlyInstallment).HasColumnType("decimal(18,2)");
        builder.Property(s => s.TransferFeePercentage).HasColumnType("decimal(5,2)");

        builder.Property(s => s.PaymentMethod).IsRequired().HasMaxLength(30)
            .HasDefaultValue("Cash");
        builder.Property(s => s.InstallmentNotes).HasMaxLength(500);
        builder.Property(s => s.ExtraInclusions).HasMaxLength(500);

        // One-to-One مع Property
        builder.HasOne(s => s.Property)
            .WithOne(p => p.SaleDetails!)
            .HasForeignKey<SaleDetails>(s => s.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK مع Currencies
        builder.HasOne(s => s.PriceCurrency)
            .WithMany()
            .HasForeignKey(s => s.PriceCurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        // UNIQUE: كل عقار له سجل بيع واحد فقط
        builder.HasIndex(s => s.PropertyId).IsUnique();
    }
}