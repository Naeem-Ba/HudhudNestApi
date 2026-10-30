using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Bookings.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class VisitRequestConfiguration : IEntityTypeConfiguration<VisitRequest>
{
    public void Configure(EntityTypeBuilder<VisitRequest> builder)
    {
        builder.ToTable("VisitRequests");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.VisitorName).IsRequired().HasMaxLength(150);
        builder.Property(v => v.VisitorPhone).IsRequired().HasMaxLength(30);
        builder.Property(v => v.VisitorNote).HasMaxLength(500);
        builder.Property(v => v.OwnerNote).HasMaxLength(500);
        builder.Property(v => v.Status).IsRequired();
        builder.Property(v => v.ProposedAt).IsRequired();

        // Relations
        builder.HasOne(v => v.Property)
            .WithMany()
            .HasForeignKey(v => v.PropertyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(v => v.Requester)
            .WithMany()
            .HasForeignKey(v => v.RequesterId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(v => v.RequesterId);
        builder.HasIndex(v => new { v.PropertyId, v.RequesterId, v.Status });

        // At most one open request (Pending = 0 or RescheduleProposed = 5) per requester and
        // property: the same rule RequestVisitCommandHandler checks first. Without it, parallel
        // POST /api/Visits (double-click) created duplicate Pending rows (launch audit A2-002);
        // the losing insert now hits this index and the middleware answers 409.
        builder.HasIndex(v => new { v.PropertyId, v.RequesterId })
            .IsUnique()
            .HasDatabaseName("IX_VisitRequests_PropertyId_RequesterId_Open")
            .HasFilter("\"Status\" IN (0, 5) AND NOT \"IsDeleted\"");

        // Soft-delete filter
        builder.HasQueryFilter(v => !v.IsDeleted);
    }
}

