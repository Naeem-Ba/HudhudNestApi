using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Valuation.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

/// <summary>
/// Stage 5's persistence layer for the Stage 2 <see cref="ValuationOfficeInvitation"/> entity —
/// see <see cref="ValuationInquiryConfiguration"/>'s doc comment for why every relationship
/// here is a no-navigation-property HasOne&lt;T&gt;() rather than builder.HasOne(x => x.Nav).
/// </summary>
public sealed class ValuationOfficeInvitationConfiguration : IEntityTypeConfiguration<ValuationOfficeInvitation>
{
    public void Configure(EntityTypeBuilder<ValuationOfficeInvitation> builder)
    {
        builder.ToTable("ValuationOfficeInvitations");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Status).IsRequired();
        builder.Property(i => i.MatchLevel).IsRequired();
        builder.Property(i => i.SentAt).IsRequired();

        builder.HasOne<Agency>()
            .WithMany()
            .HasForeignKey(i => i.AgencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ValuationInquiry>()
            .WithMany()
            .HasForeignKey(i => i.InquiryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Stage 4's own duplicate-prevention rule (no agency invited twice for the same
        // inquiry), enforced here in the database as well as at the Application-logic level
        // OfficeMatchingService already applies — same "primary at the logic level, DB
        // constraint preferred where the project's pattern allows" split this module's rules
        // ask for. Not filtered to one status the way AgencyInvitations' pending-only index
        // is: an agency must never be invited twice for the same inquiry regardless of
        // whether the first invitation is still Sent, already Responded, or Expired.
        builder.HasIndex(i => new { i.AgencyId, i.InquiryId })
            .IsUnique()
            .HasDatabaseName("IX_ValuationOfficeInvitations_AgencyId_InquiryId");

        // Stage 5's own read pattern: "Sent invitations for this inquiry" (the
        // GetByInquiryIdAsync/GetStaleSentInvitationsAsync join) and "this agency's
        // invitations" both filter on these.
        builder.HasIndex(i => i.InquiryId);
        builder.HasIndex(i => i.AgencyId);
        builder.HasIndex(i => i.Status);

        builder.HasQueryFilter(i => !i.IsDeleted);
    }
}
