using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Bookings.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

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
        builder.HasIndex(v => v.PropertyId);
        builder.HasIndex(v => v.RequesterId);
        builder.HasIndex(v => new { v.PropertyId, v.RequesterId, v.Status });

        // Soft-delete filter
        builder.HasQueryFilter(v => !v.IsDeleted);
    }
}

