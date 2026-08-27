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

        // Optimistic concurrency (RELEASE-BLOCKERS-AR.md B-9b). Same xmin/IsRowVersion mapping
        // as PropertyConfiguration. ConfirmFeaturedListingPaymentCommandHandler and
        // ConfirmListingExtensionPaymentCommandHandler both read a fee's Status, check it is
        // still Pending, then mark it Completed and grant the paid effect (featured placement /
        // extension) -- a read-check-then-write with no locking in between. Two concurrent
        // confirmations of the same fee (double-submit, two admins) previously could both pass
        // the Pending check before either write landed, granting the paid effect twice for one
        // payment. The second SaveChangesAsync now raises DbUpdateConcurrencyException instead
        // (translated to 409 by ExceptionHandlingMiddleware), so only the first confirmation
        // wins and the second is rejected rather than silently repeated.
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

        builder.Property(t => t.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(t => t.AmountInUSD).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(t => t.ExchangeRateUsed).HasColumnType("decimal(18,6)").IsRequired();

        // Stored as strings (not integers) so the column stays human-readable in the DB
        // and so future enum members can be inserted without renumbering existing rows.
        // This mirrors the convention already used for Property's enum columns
        // (see PropertyConfiguration: ListingType, Status, Condition, etc.).
        builder.Property(t => t.TransactionType).IsRequired().HasMaxLength(30).HasConversion<string>();
        builder.Property(t => t.PaymentMethod).IsRequired().HasMaxLength(30).HasConversion<string>();
        builder.Property(t => t.Status).IsRequired().HasMaxLength(30).HasConversion<string>();

        builder.Property(t => t.ReferenceNumber).HasMaxLength(100);
        builder.Property(t => t.Notes).HasMaxLength(1000);

        // Most frequently used indexes
        builder.HasIndex(t => t.PropertyId);
        builder.HasIndex(t => t.PayerId);
        builder.HasIndex(t => t.ReceiverId);
        builder.HasIndex(t => t.Status);
        builder.HasIndex(t => t.TransactedAt);
        builder.HasIndex(t => t.ReferenceNumber).IsUnique().HasFilter("\"ReferenceNumber\" IS NOT NULL");
    }
}
