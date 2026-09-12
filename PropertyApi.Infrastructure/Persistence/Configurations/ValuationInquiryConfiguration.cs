using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Lookups.Entities;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Domain.Valuation.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

/// <summary>
/// Stage 5's persistence layer for the Stage 2 <see cref="ValuationInquiry"/> entity — the
/// entity itself declares no navigation properties (Domain-only, no cross-module object graph
/// yet, per its own doc comment), so every relationship here is wired the same "no nav
/// property" way AgencyInvitationConfiguration wires AgencyId/InviterUserId/TargetUserId.
/// </summary>
public sealed class ValuationInquiryConfiguration : IEntityTypeConfiguration<ValuationInquiry>
{
    public void Configure(EntityTypeBuilder<ValuationInquiry> builder)
    {
        builder.ToTable("ValuationInquiries");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.GovernorateId).IsRequired();
        builder.Property(i => i.RequestType).IsRequired();
        builder.Property(i => i.Status).IsRequired();
        builder.Property(i => i.ExpiresAt).IsRequired();

        // Nullable FK (guest inquiries have no RequesterId) — Restrict, matching every other
        // UserAccount-owning relationship in the schema (Agency.OwnerUserId, etc.).
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(i => i.RequesterId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict, not SetNull/Cascade: these are fixed seed reference tables (Governorate/
        // District/Neighborhood) that are never deleted in normal operation — same reasoning
        // AgencyConfiguration already documents for its own identical relationships.
        builder.HasOne<Governorate>()
            .WithMany()
            .HasForeignKey(i => i.GovernorateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<District>()
            .WithMany()
            .HasForeignKey(i => i.DistrictId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Neighborhood>()
            .WithMany()
            .HasForeignKey(i => i.NeighborhoodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => i.RequesterId);

        // The one index Stage 5's expiry sweep actually needs: "non-terminal rows past their
        // ExpiresAt", read straight off this composite instead of a table scan.
        builder.HasIndex(i => new { i.Status, i.ExpiresAt })
            .HasDatabaseName("IX_ValuationInquiries_Status_ExpiresAt");

        builder.HasQueryFilter(i => !i.IsDeleted);
    }
}
