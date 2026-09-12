using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Valuation.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

/// <summary>
/// Stage 9's persistence layer for <see cref="ValuationContactConsent"/> — see
/// ValuationInquiryConfiguration's doc comment for why every relationship here is a
/// no-navigation-property HasOne&lt;T&gt;() rather than builder.HasOne(x => x.Nav).
/// </summary>
public sealed class ValuationContactConsentConfiguration : IEntityTypeConfiguration<ValuationContactConsent>
{
    public void Configure(EntityTypeBuilder<ValuationContactConsent> builder)
    {
        builder.ToTable("ValuationContactConsents");

        builder.HasKey(c => c.Id);

        // Same max lengths as Agency.ContactPhone/ContactEmail, for consistency.
        builder.Property(c => c.ContactPhone).HasMaxLength(50);
        builder.Property(c => c.ContactEmail).HasMaxLength(320);

        builder.Property(c => c.ConsentedAt).IsRequired();

        builder.HasOne<ValuationInquiry>()
            .WithMany()
            .HasForeignKey(c => c.InquiryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ValuationOfficeInvitation>()
            .WithMany()
            .HasForeignKey(c => c.InvitationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Agency>()
            .WithMany()
            .HasForeignKey(c => c.AgencyId)
            .OnDelete(DeleteBehavior.Restrict);

        // At most one consent per invitation, ever — same "logic first (the handler's own
        // idempotent get-or-create check), DB constraint as the backstop against a concurrent
        // double-submit race" split ValuationOfficeResponseConfiguration's own unique index
        // documents for itself.
        builder.HasIndex(c => c.InvitationId)
            .IsUnique()
            .HasDatabaseName("IX_ValuationContactConsents_InvitationId");

        // The office dashboard's per-agency read path (Stage 9 addition to
        // GetMyAgencyValuationInquiriesQuery) looks up "which of my invitations have consent"
        // by AgencyId.
        builder.HasIndex(c => c.AgencyId)
            .HasDatabaseName("IX_ValuationContactConsents_AgencyId");

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}
