using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class RentalDetailsConfiguration : IEntityTypeConfiguration<RentalDetails>
{
    public void Configure(EntityTypeBuilder<RentalDetails> builder)
    {
        builder.ToTable("RentalDetails");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.MonthlyRent).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(r => r.SecurityDepositAmount).HasColumnType("decimal(18,2)");
        builder.Property(r => r.PaymentFrequency).IsRequired().HasMaxLength(30)
            .HasDefaultValue("Monthly");
        builder.Property(r => r.AllowedTenantType).HasMaxLength(50);
        builder.Property(r => r.RenewalPolicy).HasMaxLength(200);

        // One-to-One مع Property
        builder.HasOne(r => r.Property)
            .WithOne(p => p.RentalDetails!)
            .HasForeignKey<RentalDetails>(r => r.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK مع Currencies
        builder.HasOne(r => r.RentCurrency)
            .WithMany()
            .HasForeignKey(r => r.RentCurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.PropertyId).IsUnique();
    }
}