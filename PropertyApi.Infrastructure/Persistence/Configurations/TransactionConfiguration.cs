using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Transactions.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(t => t.AmountInUSD).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(t => t.ExchangeRateUsed).HasColumnType("decimal(18,6)").IsRequired();
        builder.Property(t => t.TransactionType).IsRequired().HasMaxLength(50);
        builder.Property(t => t.PaymentMethod).IsRequired().HasMaxLength(50);
        builder.Property(t => t.Status).IsRequired().HasMaxLength(30)
            .HasDefaultValue("Pending");
        builder.Property(t => t.ReferenceNumber).HasMaxLength(100);
        builder.Property(t => t.Notes).HasMaxLength(1000);

        // الفهارس الأكثر استخداماً
        builder.HasIndex(t => t.PropertyId);
        builder.HasIndex(t => t.PayerId);
        builder.HasIndex(t => t.ReceiverId);
        builder.HasIndex(t => t.Status);
        builder.HasIndex(t => t.TransactedAt);
        builder.HasIndex(t => t.ReferenceNumber).IsUnique().HasFilter("\"ReferenceNumber\" IS NOT NULL");
    }
}