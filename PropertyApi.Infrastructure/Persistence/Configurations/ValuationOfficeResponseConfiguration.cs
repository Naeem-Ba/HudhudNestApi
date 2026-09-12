using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Valuation.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

/// <summary>
/// Stage 6's persistence layer for the Stage 2 <see cref="ValuationOfficeResponse"/> entity —
/// see ValuationInquiryConfiguration's doc comment for why every relationship here is a
/// no-navigation-property HasOne&lt;T&gt;() rather than builder.HasOne(x => x.Nav).
/// </summary>
public sealed class ValuationOfficeResponseConfiguration : IEntityTypeConfiguration<ValuationOfficeResponse>
{
    public void Configure(EntityTypeBuilder<ValuationOfficeResponse> builder)
    {
        builder.ToTable("ValuationOfficeResponses");

        builder.HasKey(r => r.Id);

        // decimal(18,4) — same precision Property's own price fields
        // (ColdRent/WarmRent/PurchasePrice) already use.
        builder.Property(r => r.EstimatedPrice)
            .HasColumnType("decimal(18,4)")
            .IsRequired();

        builder.Property(r => r.Notes)
            .HasMaxLength(2000);

        builder.Property(r => r.SubmittedAt).IsRequired();

        builder.HasOne<ValuationOfficeInvitation>()
            .WithMany()
            .HasForeignKey(r => r.InvitationId)
            .OnDelete(DeleteBehavior.Restrict);

        // At most one response per invitation, ever — MarkResponded's own Sent-only guard is
        // the primary defense (Application-logic level, per this module's "logic first, DB
        // constraint preferred" duplicate-prevention split), this index is the DB-level
        // backstop against the read-then-write race two concurrent submissions could otherwise
        // hit (ValuationOfficeInvitation carries no optimistic-concurrency token).
        builder.HasIndex(r => r.InvitationId)
            .IsUnique()
            .HasDatabaseName("IX_ValuationOfficeResponses_InvitationId");

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}
